namespace DesControlCenter.App.ViewModels;

public sealed record ProjectOption(Guid? Id, string Name)
{
    public override string ToString() => Name;
}
