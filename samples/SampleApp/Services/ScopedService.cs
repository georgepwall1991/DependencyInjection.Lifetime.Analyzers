namespace SampleApp.Services;

public class ScopedService : IScopedService
{
    public string DoWork()
    {
        Console.WriteLine("ScopedService.DoWork()");
        return "Done";
    }
}
