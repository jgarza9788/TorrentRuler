namespace TorrentRuler.Web.Pages.Shared;

/// <summary>What an empty list shows instead of a bare "nothing here": an icon, a title, one line of help, and the action that fills it.</summary>
/// <param name="Icon">Tabler icon name without the "ti-" prefix.</param>
public sealed record EmptyStateModel(string Icon, string Title, string Text, string? ActionText = null, string? ActionUrl = null);
