namespace SampleApp.Services;

public class SingletonService : ISingletonService
{
    public void Execute() => Console.WriteLine("SingletonService.Execute()");
}
