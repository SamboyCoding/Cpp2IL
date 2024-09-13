using System.Collections.Generic;
using System.Linq;

namespace Cpp2IL.Core.Graphs.Analysis.Stack;

// Avert your eyes... I'm just trying to get this to work
internal sealed class StackEntry
{
    public Stack<string> StackState = [];

    public int Size => StackState.Count;

    public void PushEntry(string value) => StackState.Push(value);

    public string PopEntry() => StackState.Pop();

    public static StackEntry Copy(StackEntry other)
    {
        var newEntry = new StackEntry();

        foreach (var entry in other.StackState.Reverse())
            newEntry.PushEntry(entry);

        return newEntry;
    }

    public StackEntry Clone() 
    {
        return Copy(this);
    }
}
