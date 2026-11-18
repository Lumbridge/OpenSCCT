namespace OpenScct.Master;

internal sealed partial class AccountStore
{
    // Older databases keep submission receipts for 30 days. Fresh databases have no receipt table.
    public void PruneLegacyReportReceipts()
    {
        lock (_gate)
        {
            if (Scalar("SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name = 'website_bug_reports';") is not null)
                Exec("DELETE FROM website_bug_reports WHERE created <= $cutoff;", ("$cutoff", Now - 30 * 86400));
        }
    }
}
