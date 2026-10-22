using GlassDesk.Platform;

internal static class HomeDirectoryChecks
{
    public static async Task RunAsync(Action<bool,string> check)
    {
        using var release=new ManualResetEventSlim();
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads=0;
        var service=new HomeDirectoryService(index=>
        {
            if(index==0){Interlocked.Increment(ref reads);started.TrySetResult();release.Wait();}
            return new("folder"+index,"C:/fixture/"+index,[]);
        });
        try
        {
            using var firstCancellation=new CancellationTokenSource();
            var first=service.ReadAsync(0,firstCancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            firstCancellation.Cancel();
            try{await first;}catch(OperationCanceledException){ }
            using var secondCancellation=new CancellationTokenSource();
            var repeated=service.ReadAsync(0,secondCancellation.Token);
            secondCancellation.Cancel();
            try{await repeated;}catch(OperationCanceledException){ }
            check(reads==1,"cancelled/repeated folder reads share one blocked OS operation");
            var other=await service.ReadAsync(1,CancellationToken.None);
            check(other.Name=="folder1"&&reads==1,"one unavailable directory does not block other local directories");
        }
        finally{release.Set();}
        // Wait until the reader has left the fixture before disposing its wait handle.
        await service.ReadAsync(0,CancellationToken.None);
    }
}
