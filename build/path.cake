public static class Paths
{
    public static FilePath SolutionFile => "Linker.sln";

    public static FilePath TestProjectFile => "test/Linker.Tests/Linker.Tests.csproj";

    public static DirectoryPath WebProjectDirectory => "src/Linker";

    public static FilePath WebProjectFile => "src/Linker/Linker.csproj";

    public static DirectoryPath PublishDirectory => "publish";

    public static DirectoryPath TestResultsDirectory => "testResults";
}