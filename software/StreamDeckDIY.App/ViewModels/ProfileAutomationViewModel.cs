using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using StreamDeckDIY.Core.Profiles;
using StreamDeckDIY.Core.Profiles.Automation;

namespace StreamDeckDIY.App.ViewModels;

public sealed class ProfileAutomationViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IAutoProfileService service;
    private readonly IProfileService profiles;
    private readonly DispatcherQueue dispatcher;
    private bool suppressEnabled;
    private bool autoSwitchEnabled;
    private bool isBusy = true;
    private ProfileActivationRuleItem? selectedRule;
    private string processName = string.Empty;
    private StreamDeckProfile? selectedTargetProfile;
    private bool ruleEnabled = true;
    private string feedback = string.Empty;

    public ProfileAutomationViewModel(
        IAutoProfileService service,
        IProfileService profiles,
        DispatcherQueue dispatcher)
    {
        this.service = service;
        this.profiles = profiles;
        this.dispatcher = dispatcher;
        service.FeedbackAvailable += OnFeedbackAvailable;
        service.ProfileActivated += OnProfileActivated;
        NewRuleCommand = new AsyncRelayCommand(_ => NewRuleAsync());
        SaveRuleCommand = new AsyncRelayCommand(_ => SaveRuleAsync());
        DeleteRuleCommand = new AsyncRelayCommand(_ => DeleteRuleAsync());
    }

    public event EventHandler? ActiveProfileChanged;
    public ObservableCollection<ProfileActivationRuleItem> Rules { get; } = [];
    public ObservableCollection<StreamDeckProfile> AvailableProfiles { get; } = [];
    public ICommand NewRuleCommand { get; }
    public ICommand SaveRuleCommand { get; }
    public ICommand DeleteRuleCommand { get; }

    public bool AutoSwitchEnabled
    {
        get => autoSwitchEnabled;
        set
        {
            if (!SetProperty(ref autoSwitchEnabled, value) || suppressEnabled) return;
            _ = SetEnabledAsync(value);
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value)) OnPropertyChanged(nameof(CanEdit));
        }
    }
    public bool CanEdit => !IsBusy;
    public Visibility RulesEmptyVisibility => Rules.Count == 0
        ? Visibility.Visible : Visibility.Collapsed;

    public ProfileActivationRuleItem? SelectedRule
    {
        get => selectedRule;
        set
        {
            if (!SetProperty(ref selectedRule, value) || value is null) return;
            ProcessName = value.Rule.ProcessName;
            SelectedTargetProfile = AvailableProfiles.FirstOrDefault(
                profile => profile.Id == value.Rule.ProfileId);
            RuleEnabled = value.Rule.Enabled;
        }
    }

    public string ProcessName
    {
        get => processName;
        set => SetProperty(ref processName, value);
    }
    public StreamDeckProfile? SelectedTargetProfile
    {
        get => selectedTargetProfile;
        set => SetProperty(ref selectedTargetProfile, value);
    }
    public bool RuleEnabled
    {
        get => ruleEnabled;
        set => SetProperty(ref ruleEnabled, value);
    }
    public string Feedback
    {
        get => feedback;
        private set => SetProperty(ref feedback, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            await service.InitializeAsync(cancellationToken);
            suppressEnabled = true;
            AutoSwitchEnabled = service.AutoSwitchEnabled;
            suppressEnabled = false;
            RefreshProfiles();
            RefreshRules();
            Feedback = service.LoadWarning ?? string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        service.StartAsync(cancellationToken);

    public Task ReevaluateAsync(CancellationToken cancellationToken) =>
        service.EvaluateCurrentAsync(cancellationToken);

    public void RefreshProfiles()
    {
        var selectedId = SelectedTargetProfile?.Id;
        AvailableProfiles.Clear();
        foreach (var profile in profiles.Profiles) AvailableProfiles.Add(profile);
        SelectedTargetProfile = AvailableProfiles.FirstOrDefault(
            profile => profile.Id == selectedId) ?? AvailableProfiles.FirstOrDefault();
        RefreshRules();
    }

    public async ValueTask DisposeAsync()
    {
        service.FeedbackAvailable -= OnFeedbackAvailable;
        service.ProfileActivated -= OnProfileActivated;
        await service.DisposeAsync();
    }

    private async Task SetEnabledAsync(bool enabled)
    {
        IsBusy = true;
        try
        {
            await service.SetEnabledAsync(enabled);
            Feedback = enabled ? "Cambio automático activado." : "Cambio automático desactivado.";
        }
        catch (Exception exception)
        {
            suppressEnabled = true;
            AutoSwitchEnabled = service.AutoSwitchEnabled;
            suppressEnabled = false;
            Feedback = $"No se pudo guardar la opción: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task NewRuleAsync()
    {
        SelectedRule = null;
        ProcessName = string.Empty;
        SelectedTargetProfile = AvailableProfiles.FirstOrDefault();
        RuleEnabled = true;
        Feedback = "Introduce la aplicación y el perfil.";
        return Task.CompletedTask;
    }

    private async Task SaveRuleAsync()
    {
        if (IsBusy || SelectedTargetProfile is null) return;
        IsBusy = true;
        try
        {
            ProfileActivationRule saved;
            if (SelectedRule is null)
            {
                saved = await service.CreateRuleAsync(
                    ProcessName, SelectedTargetProfile.Id, RuleEnabled);
            }
            else
            {
                saved = SelectedRule.Rule with
                {
                    ProcessName = ProcessName,
                    ProfileId = SelectedTargetProfile.Id,
                    Enabled = RuleEnabled,
                };
                await service.UpdateRuleAsync(saved);
            }
            RefreshRules(saved.Id);
            Feedback = "Regla guardada.";
        }
        catch (Exception exception)
        {
            Feedback = $"No se pudo guardar la regla: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteRuleAsync()
    {
        if (IsBusy || SelectedRule is null) return;
        IsBusy = true;
        try
        {
            await service.DeleteRuleAsync(SelectedRule.Rule.Id);
            RefreshRules();
            await NewRuleAsync();
            Feedback = "Regla eliminada.";
        }
        catch (Exception exception)
        {
            Feedback = $"No se pudo eliminar la regla: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshRules(uint? selectedId = null)
    {
        var id = selectedId ?? SelectedRule?.Rule.Id;
        Rules.Clear();
        foreach (var rule in service.Rules)
        {
            var profile = profiles.Profiles.FirstOrDefault(
                value => value.Id == rule.ProfileId);
            Rules.Add(new ProfileActivationRuleItem(
                rule, profile?.Name ?? "Perfil no disponible", profile is not null));
        }
        SelectedRule = Rules.FirstOrDefault(rule => rule.Rule.Id == id);
        OnPropertyChanged(nameof(RulesEmptyVisibility));
    }

    private void OnFeedbackAvailable(
        object? sender, AutoProfileFeedbackEventArgs eventArgs) =>
        dispatcher.TryEnqueue(() => Feedback = eventArgs.Message);

    private void OnProfileActivated(object? sender, EventArgs eventArgs) =>
        dispatcher.TryEnqueue(() => ActiveProfileChanged?.Invoke(this, EventArgs.Empty));
}

public sealed record ProfileActivationRuleItem(
    ProfileActivationRule Rule,
    string ProfileName,
    bool IsProfileValid)
{
    public string ProcessDisplayName => string.IsNullOrWhiteSpace(Rule.ProcessName)
        ? "Aplicación sin definir" : Rule.ProcessName;
    public string EnabledText => Rule.Enabled ? "ACTIVA" : "PAUSADA";
    public string Status => IsProfileValid
        ? $"{Rule.ProcessName}  →  {ProfileName}{(Rule.Enabled ? string.Empty : "  (desactivada)")}" 
        : $"{Rule.ProcessName}  →  Perfil no disponible";
}
