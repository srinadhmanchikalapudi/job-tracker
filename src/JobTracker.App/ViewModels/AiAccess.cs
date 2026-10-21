using System.Net.Http;
using JobTracker.Core.Ai;

namespace JobTracker.App.ViewModels;

/// <summary>What every AI feature does before it sends anything: the model the person set up, and their one-time agreement to send text to it.</summary>
public static class AiAccess
{
    /// <summary>One client for every AI request, so connections are reused.</summary>
    public static HttpClient Http { get; set; } = new() { Timeout = TimeSpan.FromSeconds(90) };

    /// <summary>
    /// The model to ask, or null with a sentence saying why not: no key saved, or the person said no. The first time for a service, the person is asked
    /// (<paramref name="confirmSend"/> gets the destination and says whether they agree), and the answer is remembered.
    /// </summary>
    public static IChatModel? Prepare(MainViewModel main, Func<string, bool> confirmSend, out string problem)
    {
        var model = main.CreateChatModel(Http, out problem);
        if (model is null)
            return null;

        if (!main.HasAiConsent)
        {
            if (!confirmSend(main.AiDestination))
            {
                problem = "Nothing was sent.";
                return null;
            }

            main.GiveAiConsent();
        }

        return model;
    }
}
