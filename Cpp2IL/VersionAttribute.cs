using System;

namespace Cpp2IL
{
    [AttributeUsage(AttributeTargets.Field)]
    class VersionAttribute : Attribute
    {
        public float Min { get; set; } = 0;
        public float Max { get; set; } = 99;
    }
}