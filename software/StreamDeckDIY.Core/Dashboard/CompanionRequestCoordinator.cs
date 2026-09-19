namespace StreamDeckDIY.Core.Dashboard;

public readonly record struct CompanionRequestLease(long Version,string Key);

public sealed class CompanionRequestCoordinator
{
    private readonly object sync=new();
    private long version;
    private string? processingKey;
    private string? appliedKey;

    public bool TryBegin(string key,out CompanionRequestLease lease)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock(sync)
        {
            if(key==appliedKey||key==processingKey)
            {
                lease=default;
                return false;
            }
            processingKey=key;
            lease=new(++version,key);
            return true;
        }
    }

    public bool IsCurrent(CompanionRequestLease lease)
    {
        lock(sync)return lease.Version==version&&lease.Key==processingKey;
    }

    public void Complete(CompanionRequestLease lease)
    {
        lock(sync)
        {
            if(lease.Version!=version||lease.Key!=processingKey)return;
            appliedKey=lease.Key;
            processingKey=null;
        }
    }

    public void Abandon(CompanionRequestLease lease)
    {
        lock(sync)
            if(lease.Version==version&&lease.Key==processingKey)
                processingKey=null;
    }

    public void Invalidate()
    {
        lock(sync)
        {
            version++;
            processingKey=null;
            appliedKey=null;
        }
    }
}