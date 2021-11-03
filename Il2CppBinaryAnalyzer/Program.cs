using System;
using System.IO;
using System.Linq;
using LibCpp2IL;
using LibCpp2IL.Elf;
using LibCpp2IL.PE;

namespace Il2CppBinaryAnalyzer
{
    class Program
    {
        public static Il2CppBinary? Binary; 
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: Il2CppBinaryAnalyzer [file]");
                return 1;
            }

            var binaryPath = args[0];
            var binaryBytes = File.ReadAllBytes(binaryPath);
            
            ulong codereg, metareg;
            if (BitConverter.ToInt16(binaryBytes.Take(2).ToArray(), 0) == 0x5A4D)
            {
                var pe = new PE(new MemoryStream(binaryBytes, 0, binaryBytes.Length, false, true), 0);
                Binary = pe;

                (codereg, metareg) = pe.PlusSearch(0x10_000, 0);
            } else if (BitConverter.ToInt32(binaryBytes.Take(4).ToArray(), 0) == 0x464c457f)
            {
                var elf = new ElfFile(new MemoryStream(binaryBytes, 0, binaryBytes.Length, true, true), 0);
                Binary = elf;
                (codereg, metareg) = elf.FindCodeAndMetadataReg();
            }
            else
            {
                throw new Exception("Unknown binary type");
            }
            
            Console.WriteLine($"Code and meta reg resolved to 0x{codereg:X}, 0x{metareg:X}");

            Console.ReadLine();
            return 0;
        }
    }
}