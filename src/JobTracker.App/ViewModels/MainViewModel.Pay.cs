using System.IO;
using JobTracker.Core;
using JobTracker.Core.Pay;

namespace JobTracker.App.ViewModels;

/// <summary>The part of the main view model that fills in the pay shown in the list, without making the window wait for it.</summary>
public sealed partial class MainViewModel
{
    private CancellationTokenSource? _payLoad;

    /// <summary>
    /// The pay of every application in the list. What is already remembered (and still valid) appears at once; the rest is read from the job
    /// descriptions one at a time in the background and remembered for next time. A new load, such as a change of folder, stops the old one.
    /// </summary>
    private void LoadListPay(List<ApplicationItemVM> items)
    {
        _payLoad?.Cancel();
        var cts = _payLoad = new CancellationTokenSource();
        var store = _store;
        var cache = new PayCache(store.Root);

        var unknown = new List<ApplicationItemVM>();
        foreach (var item in items)
        {
            if (store.TryKnownPay(item.App, cache, out var pay))
                item.Pay = pay;
            else
                unknown.Add(item);
        }

        if (unknown.Count == 0)
            return;

        _ = Task.Run(() =>
        {
            foreach (var item in unknown)
            {
                if (cts.IsCancellationRequested)
                    return;

                PayInfo? pay;
                try
                {
                    pay = store.ReadPayCached(item.App, cache);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue; // a file that cannot be opened right now (OneDrive busy): leave this one blank and carry on
                }

                // The window's own thread sets what it shows.
                _ = _dispatcher.BeginInvoke(() =>
                {
                    if (!cts.IsCancellationRequested)
                        item.Pay = pay;
                });
            }

            if (!cts.IsCancellationRequested)
                cache.Save();
        });
    }
}