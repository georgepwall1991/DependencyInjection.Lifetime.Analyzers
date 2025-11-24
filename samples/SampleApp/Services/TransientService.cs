namespace SampleApp.Services;

public class TransientService : ITransientService
{
    public void Process() => Console.WriteLine("TransientService.Process()");
}
