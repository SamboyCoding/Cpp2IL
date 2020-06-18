namespace Cpp2IL.Analysis
{
    internal partial class AsmDumper
    {
        private class StackPointer
        {
            public readonly int Address;

            public StackPointer(int address)
            {
                Address = address;
            }
        }
    }
}