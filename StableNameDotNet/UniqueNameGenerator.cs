using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StableNameDotNet
{
    /// <summary>
    /// Generates a string, based on input, that can be used to somewhat distinguish between otherwise-similar types, based off of
    /// type references occuring within it, and how many times they occur.
    /// </summary>
    public class UniqueNameGenerator
    {
        /// <summary>
        /// The number of characters from each input to use in the unique name. Do NOT change this without also calling <see cref="StableNameGenerator.ResetForNewConfig"/>
        /// </summary>
        public static int NumCharsToTakeFromEachInput = 2;
        public static int MaxInputs = 10;
        
        private readonly object _lock = new();

        private readonly Dictionary<string, int> _inputOccurrenceCounts = new();
        private readonly SortedSet<Input> _resultSet = new();

        /// <summary>
        /// Returns true if the generator is full, and will not process any more inputs.
        /// </summary>
        public bool IsFull => _resultSet.Count >= MaxInputs;

        /// <summary>
        /// Adds an input to the generator.
        /// </summary>
        /// <param name="input">The input to add</param>
        /// <returns>True if this generator can still accept more input, based on the <see cref="MaxInputs"/> field, false if it is full.</returns>
        public bool PushInput(string input)
        {
            lock (_lock)
            {
                //We can't actually abort on full because unhollower doesn't, and we have to match its names.
                // if (IsFull)
                    // return false;

                if (input.ContainsAnyInvalidSourceCodeChars())
                    return true; //Not full

                var key = input.Substring(0, Math.Min(input.Length, NumCharsToTakeFromEachInput));
                var numTimesKeyOccurred = _inputOccurrenceCounts[key] = _inputOccurrenceCounts.GetOrCreate(key, () => 0) + 1;
                var numKeysSeen = _inputOccurrenceCounts.Count;
                var chronologicalOrderFactor = _resultSet.Count / 100f;

                var weight = numKeysSeen + numTimesKeyOccurred * 2 + chronologicalOrderFactor;

                _resultSet.Add(new(key, weight));

                return true;
            }
        }
        
        /// <summary>
        /// Adds all of the inputs in the given collection to the generator.
        /// </summary>
        /// <param name="inputs">The inputs to add</param>
        /// <returns>True if, after adding all the inputs, the generator still has room for more. False if it full, as determined by the <see cref="MaxInputs"/> field.</returns>
        public bool PushInputs(List<string> inputs) => inputs.All(PushInput);

        /// <summary>
        /// Generates the final string, based on the inputs that have been added to the generator.
        /// </summary>
        public string GenerateUniqueName()
            => string.Join("", _resultSet.Select(x => x.Value));

        private struct Input : IComparable<Input>
        {
            public string Value;
            public float Weight;

            public Input(string value, float weight)
            {
                Value = value;
                Weight = weight;
            }

            public int CompareTo(Input other)
            {
                return Weight.CompareTo(other.Weight);
            }
        }
    }
}