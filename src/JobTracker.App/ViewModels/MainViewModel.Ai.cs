using System.IO;
using JobTracker.Core;
using JobTracker.Core.Ai;

namespace JobTracker.App.ViewModels;

/// <summary>The part of the main view model that holds the AI assistant's settings. The key itself lives in Windows Credential Manager, never in a file.</summary>
public sealed partial class MainViewModel
{
    public static IReadOnlyList<string> AiProviderOptions => AiProviders.All;

    /// <summary>Where the API key is kept. Windows Credential Manager in the app; a stand-in in tests.</summary>
    public ISecretStore Secrets { get; set; } = new WindowsCredentialStore();

    public AppSettings Settings => _settings;

    // ---- the model drop-down
    public System.Collections.ObjectModel.ObservableCollection<string> AvailableModels { get; } = [];

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string _modelsStatus = "";

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(CanRefreshModels))]
    private bool _isLoadingModels;

    public bool CanRefreshModels => !IsLoadingModels;

    public string AiProvider
    {
        get => AiProviders.Normalize(_settings.AiProvider);
        set
        {
            var provider = AiProviders.Normalize(value);
            if (provider == AiProvider)
                return;
            _settings.AiProvider = provider;
            _settings.AiModel = null; // each service names its models differently
            Try(() => _settings.Save());
            OnPropertyChanged();
            RaiseAiChanged();
            LoadModelsOnOpen();
        }
    }

    /// <summary>
    /// The model to ask. Until the person picks another, this is the service's usual model; it is shown in the box but not stored, so it keeps following
    /// the usual model if that changes.
    /// </summary>
    public string AiModel
    {
        get => _settings.AiModel ?? AiProviders.DefaultModel(AiProvider);
        set
        {
            var model = value.Trim();
            if (model == AiModel)
                return;
            _settings.AiModel = model.Length == 0 || model == AiProviders.DefaultModel(AiProvider) ? null : model;
            Try(() => _settings.Save());
            OnPropertyChanged();
            OnPropertyChanged(nameof(AiModelHint));
        }
    }

    public string AiModelHint => _settings.AiModel is null
        ? "This is the usual model. Pick another from the list, or type any model name."
        : $"Clear the box to go back to the usual model ({AiProviders.DefaultModel(AiProvider)}).";

    public string AiDestination => AiProviders.Destination(AiProvider);

    public bool HasAiKey => ReadKey() is not null;

    public string AiKeyStatus => HasAiKey
        ? $"A key for {AiProvider} is saved in Windows Credential Manager."
        : $"No key saved for {AiProvider} yet.";

    private string? ReadKey()
    {
        try
        {
            return Secrets.Get(AiProviders.SecretName(AiProvider)) is { Length: > 0 } key ? key : null;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public void SaveAiKey(string key)
    {
        var trimmed = key.Trim();
        if (trimmed.Length == 0)
            return;
        Secrets.Set(AiProviders.SecretName(AiProvider), trimmed);
        RaiseAiChanged();
        ShowToast("Key saved");
        LoadModelsOnOpen(); // with a key, the service can now list its models
    }

    public void RemoveAiKey()
    {
        Secrets.Remove(AiProviders.SecretName(AiProvider));
        RaiseAiChanged();
        ShowToast("Key removed");
    }

    /// <summary>
    /// Fills the model drop-down: the service's own list when it can be asked (OpenRouter's is public; Anthropic's needs the saved key), and the usual
    /// names otherwise. Called when Settings opens and when the service or key changes. Nothing personal is sent.
    /// </summary>
    public void LoadModelsOnOpen()
    {
        ShowModels(ModelCatalog.Fallback(AiProvider));
        if (ModelCatalog.NeedsKey(AiProvider) && !HasAiKey)
        {
            ModelsStatus = $"Save your {AiProvider} key to list its models. Showing the usual ones.";
            return;
        }

        ModelsStatus = $"Showing the usual models. Asking {AiProvider} for its list…";
        _ = RefreshModelsAsync();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task RefreshModelsAsync()
    {
        var provider = AiProvider;
        var key = ReadKey();
        return _dispatcher.InvokeAsync(async () =>
        {
            IsLoadingModels = true;
            ModelsStatus = $"Asking {provider} for its models…";
            IReadOnlyList<string>? models = null;
            string? error = null;
            try
            {
                models = await Task.Run(() => ModelCatalog.ListAsync(provider, key, AiAccess.Http));
            }
            catch (ModelException ex)
            {
                error = ex.Message;
            }

            IsLoadingModels = false;
            if (provider != AiProvider)
                return; // the person switched service while this was running: that answer is for the other one

            if (error is not null)
            {
                ShowModels(ModelCatalog.Fallback(provider));
                ModelsStatus = error + " Showing the usual models instead.";
                return;
            }

            ShowModels(models!);
            var blankDefaultMissing = _settings.AiModel is null && !models!.Contains(AiProviders.DefaultModel(provider), StringComparer.OrdinalIgnoreCase);
            ModelsStatus = $"{models!.Count} models from {provider}." +
                           (blankDefaultMissing ? $" The usual default ({AiProviders.DefaultModel(provider)}) is not in the list, so pick a model." : "");
        }).Task.Unwrap();
    }

    private void ShowModels(IEnumerable<string> models)
    {
        AvailableModels.Clear();
        var list = models.ToList();
        // A model typed in by hand that the list does not know stays visible in it.
        if (AiModel.Length > 0 && !list.Contains(AiModel, StringComparer.OrdinalIgnoreCase))
            list.Insert(0, AiModel);
        foreach (var model in list)
            AvailableModels.Add(model);

        // Emptying the list makes an editable drop-down blank its text; tell the box what the model is so it shows it again (and does not write a blank back).
        OnPropertyChanged(nameof(AiModel));
    }

    private void RaiseAiChanged()
    {
        OnPropertyChanged(nameof(AiModel));
        OnPropertyChanged(nameof(AiModelHint));
        OnPropertyChanged(nameof(AiDestination));
        OnPropertyChanged(nameof(HasAiKey));
        OnPropertyChanged(nameof(AiKeyStatus));
    }

    /// <summary>The model the person set up, or null with a sentence saying what is missing.</summary>
    public IChatModel? CreateChatModel(System.Net.Http.HttpClient http, out string problem) =>
        ChatModelFactory.TryCreate(_settings, Secrets, http, out problem);

    public bool HasAiConsent => _settings.HasAiConsent(AiProvider);

    public void GiveAiConsent()
    {
        _settings.GiveAiConsent(AiProvider);
        Try(() => _settings.Save());
    }
}
