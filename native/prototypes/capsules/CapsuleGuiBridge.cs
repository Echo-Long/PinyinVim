// Prototype presentation adapter. Pass RPC through byte-for-byte except
// fractional win_float_pos positions: Neovide 0.16 selects integer screen
// coordinates when the optional compositor fields exist. Send the supported
// eight-field anchor format for these positions, preserving every grid, text,
// key, request and response. No editing logic or clipboard logic lives here.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

internal static class CapsuleGuiBridge
{
    sealed class Value
    {
        internal int Start, End;
        internal List<Value> Array;
        internal string Text;
        internal double? Number;
    }

    static void Need(byte[] data, ref int offset, long count)
    {
        if (count < 0 || count > 64 * 1024 * 1024) throw new InvalidDataException("Oversized RPC item");
        if (offset + count > data.Length) throw new EndOfStreamException();
        offset += (int)count;
    }
    static uint Unsigned(byte[] data, ref int offset, int bytes)
    {
        if (offset + bytes > data.Length) throw new EndOfStreamException();
        uint value = 0;
        for (int i = 0; i < bytes; i++) value = (value << 8) | data[offset++];
        return value;
    }
    static Value ReadValue(byte[] data, ref int offset, int depth)
    {
        if (depth > 100) throw new InvalidDataException("RPC nesting limit");
        if (offset >= data.Length) throw new EndOfStreamException();
        int start = offset;
        byte type = data[offset++];
        int arrays = -1, maps = -1;
        long length = -1;
        Value result = new Value { Start = start };
        if (type <= 0x7f) result.Number = type;
        else if (type >= 0xe0) result.Number = (sbyte)type;
        else if ((type & 0xf0) == 0x90) arrays = type & 15;
        else if ((type & 0xf0) == 0x80) maps = type & 15;
        else if ((type & 0xe0) == 0xa0) length = type & 31;
        else switch (type)
        {
            case 0xc0: case 0xc2: case 0xc3: break;
            case 0xc4: case 0xc7: length = Unsigned(data, ref offset, 1) + (type == 0xc7 ? 1 : 0); break;
            case 0xc5: case 0xc8: length = Unsigned(data, ref offset, 2) + (type == 0xc8 ? 1 : 0); break;
            case 0xc6: case 0xc9: length = Unsigned(data, ref offset, 4) + (type == 0xc9 ? 1 : 0); break;
            case 0xca: case 0xcb:
                int size = type == 0xca ? 4 : 8;
                int floatStart = offset; Need(data, ref offset, size);
                byte[] number = new byte[size];
                Buffer.BlockCopy(data, floatStart, number, 0, size);
                if (BitConverter.IsLittleEndian) System.Array.Reverse(number);
                result.Number = size == 4 ? BitConverter.ToSingle(number, 0) : BitConverter.ToDouble(number, 0);
                break;
            case 0xcc: result.Number = Unsigned(data, ref offset, 1); break;
            case 0xcd: result.Number = Unsigned(data, ref offset, 2); break;
            case 0xce: result.Number = Unsigned(data, ref offset, 4); break;
            case 0xcf: case 0xd3: Need(data, ref offset, 8); break;
            case 0xd0: Need(data, ref offset, 1); break;
            case 0xd1: Need(data, ref offset, 2); break;
            case 0xd2: Need(data, ref offset, 4); break;
            case 0xd4: length = 2; break;
            case 0xd5: length = 3; break;
            case 0xd6: length = 5; break;
            case 0xd7: length = 9; break;
            case 0xd8: length = 17; break;
            case 0xd9: length = Unsigned(data, ref offset, 1); break;
            case 0xda: length = Unsigned(data, ref offset, 2); break;
            case 0xdb: length = Unsigned(data, ref offset, 4); break;
            case 0xdc: arrays = (int)Unsigned(data, ref offset, 2); break;
            case 0xdd: arrays = checked((int)Unsigned(data, ref offset, 4)); break;
            case 0xde: maps = (int)Unsigned(data, ref offset, 2); break;
            case 0xdf: maps = checked((int)Unsigned(data, ref offset, 4)); break;
            default: throw new InvalidDataException("Invalid RPC type");
        }
        if (length >= 0)
        {
            int textStart = offset; Need(data, ref offset, length);
            if ((type & 0xe0) == 0xa0 || type == 0xd9 || type == 0xda || type == 0xdb)
                result.Text = Encoding.UTF8.GetString(data, textStart, (int)length);
        }
        if (arrays >= 0)
        {
            if (arrays > 1000000) throw new InvalidDataException("RPC array limit");
            result.Array = new List<Value>();
            for (int i = 0; i < arrays; i++) result.Array.Add(ReadValue(data, ref offset, depth + 1));
        }
        if (maps >= 0)
        {
            if (maps > 1000000) throw new InvalidDataException("RPC map limit");
            for (int i = 0; i < maps; i++) { ReadValue(data, ref offset, depth + 1); ReadValue(data, ref offset, depth + 1); }
        }
        result.End = offset;
        return result;
    }
    static bool Fractional(Value value) { return value.Number.HasValue && Math.Abs(value.Number.Value % 1) > .000001; }
    static bool ConvertPositions(Value root)
    {
        if (root.Array == null || root.Array.Count != 3 || root.Array[0].Number != 2 || root.Array[1].Text != "redraw")
            return false;
        bool changed = false;
        foreach (Value evt in root.Array[2].Array ?? new List<Value>())
        {
            if (evt.Array == null || evt.Array.Count < 2 || evt.Array[0].Text != "win_float_pos") continue;
            for (int i = 1; i < evt.Array.Count; i++)
            {
                var args = evt.Array[i].Array;
                if (args != null && args.Count > 8 && (Fractional(args[4]) || Fractional(args[5])))
                {
                    args.RemoveRange(8, args.Count - 8); changed = true;
                }
            }
        }
        return changed;
    }
    static void WriteValue(Stream output, byte[] bytes, Value value)
    {
        if (value.Array == null) { output.Write(bytes, value.Start, value.End - value.Start); return; }
        int count = value.Array.Count;
        if (count < 16) output.WriteByte((byte)(0x90 | count));
        else if (count <= 65535) { output.WriteByte(0xdc); output.WriteByte((byte)(count >> 8)); output.WriteByte((byte)count); }
        else { output.WriteByte(0xdd); for (int i = 3; i >= 0; --i) output.WriteByte((byte)(count >> (i * 8))); }
        foreach (Value child in value.Array) WriteValue(output, bytes, child);
    }
    static void ForwardGui(Stream input, Stream output)
    {
        byte[] buffer = new byte[65536]; int used = 0;
        for (;;)
        {
            if (used == buffer.Length)
            {
                if (used >= 64 * 1024 * 1024) throw new InvalidDataException("RPC frame limit");
                System.Array.Resize(ref buffer, buffer.Length * 2);
            }
            int received = input.Read(buffer, used, buffer.Length - used);
            if (received == 0) { if (used != 0) throw new EndOfStreamException("Truncated RPC"); return; }
            used += received; int consumed = 0;
            while (consumed < used)
            {
                // The parser sees only valid bytes, including partial frames.
                byte[] frame = new byte[used - consumed];
                Buffer.BlockCopy(buffer, consumed, frame, 0, frame.Length);
                int length = 0; Value value;
                try { value = ReadValue(frame, ref length, 0); }
                catch (EndOfStreamException) { break; }
                if (ConvertPositions(value)) WriteValue(output, frame, value);
                else output.Write(frame, 0, length);
                consumed += length;
            }
            if (consumed > 0) { Buffer.BlockCopy(buffer, consumed, buffer, 0, used - consumed); used -= consumed; output.Flush(); }
        }
    }
    static string Quote(string value)
    {
        var quoted = new StringBuilder("\""); int slashes = 0;
        foreach (char ch in value)
        {
            if (ch == '\\') slashes++;
            else { quoted.Append('\\', slashes * (ch == '"' ? 2 : 1)); slashes = 0; if (ch == '"') quoted.Append('\\'); quoted.Append(ch); }
        }
        quoted.Append('\\', slashes * 2); return quoted.Append('"').ToString();
    }
    static int Main(string[] args)
    {
        var command = new StringBuilder();
        foreach (string arg in args) { if (command.Length != 0) command.Append(' '); command.Append(Quote(arg)); }
        var start = new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nvim.exe"), command.ToString())
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true };
        using (var child = Process.Start(start))
        {
            var input = new Thread(() => {
                try {
                    var source = Console.OpenStandardInput(); var destination = child.StandardInput.BaseStream;
                    byte[] bytes = new byte[65536]; int count;
                    while ((count = source.Read(bytes, 0, bytes.Length)) > 0) {
                        destination.Write(bytes, 0, count); destination.Flush();
                    }
                    child.StandardInput.Close();
                } catch (IOException) {}
            });
            input.IsBackground = true; input.Start();
            try {
                if (System.Array.IndexOf(args, "--embed") >= 0) ForwardGui(child.StandardOutput.BaseStream, Console.OpenStandardOutput());
                else child.StandardOutput.BaseStream.CopyTo(Console.OpenStandardOutput());
                child.WaitForExit(); return child.ExitCode;
            }
            catch (Exception error) { Console.Error.WriteLine("Capsule GUI bridge: " + error.Message); if (!child.HasExited) child.Kill(); return 1; }
        }
    }
}
