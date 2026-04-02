using System;
using Force.Crc32;

static class Crc32Wrapper {
    public static uint Begin() => 0xFFFFFFFF;
    public static uint Finish(uint crc) => crc ^ 0xFFFFFFFF;
    public static uint Update(uint crc, byte[] data, int offset=0, int length=-1) {
        if(length==-1) length=data.Length-offset;
        return Crc32Algorithm.Append(crc, data, offset, length);
    }
}

class Program {
    static void Main() {
        var data = System.Text.Encoding.ASCII.GetBytes("123456789");
        uint w = Crc32Wrapper.Finish(Crc32Wrapper.Update(Crc32Wrapper.Begin(), data));
        uint d = Crc32Algorithm.Compute(data);
        Console.WriteLine($"wrapper: 0x{w:X8}");
        Console.WriteLine($"direct:  0x{d:X8}");
    }
}
