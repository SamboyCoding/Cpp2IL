namespace Cpp2IL.Core.Tests;

public static class Paths
{
    public const string TestProjectDirectory = "../../../";
    public const string RepositoryRootDirectory = "../" + TestProjectDirectory;
    public const string TestFilesDirectory = RepositoryRootDirectory + "TestFiles/";

    public static class Simple2019Game
    {
        public const string RootDirectory = TestFilesDirectory + "Simple_2019_4_34/";
        public const string DataDirectory = RootDirectory + "Simple_2019_4_34_Data/";
        public const string ExecutableFile = RootDirectory + "Simple_2019_4_34.exe";
        public const string GameAssembly = RootDirectory + "GameAssembly.dll";
        public const string Metadata = DataDirectory + "il2cpp_data/Metadata/global-metadata.dat";
    }
    
    public static class Simple2022Game
    {
        public const string RootDirectory = TestFilesDirectory + "Simple_2022_3_35/";
        public const string DataDirectory = RootDirectory + "Simple_2022_3_35_Data/";
        public const string ExecutableFile = RootDirectory + "Simple_2022_3_35.exe";
        public const string GameAssembly = RootDirectory + "GameAssembly.dll";
        public const string Metadata = DataDirectory + "il2cpp_data/Metadata/global-metadata.dat";
    }
}
