namespace DesControlCenter.App.Services;

/// <summary>UI-facing confirmation/info prompts, kept behind an interface so view models stay testable.</summary>
public interface IDialogService
{
    bool Confirm(string title, string message);
    void ShowInfo(string title, string message);
}
