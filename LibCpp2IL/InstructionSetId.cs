namespace LibCpp2IL;

public class InstructionSetId
{
    public string Name;

    public InstructionSetId(string name)
    {
            Name = name;
        }

    public override string ToString() => Name;
}