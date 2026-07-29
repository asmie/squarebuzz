namespace Squarebuzz.Core.Model;

/// <summary>
/// A themed collection of pictures.
/// </summary>
/// <param name="Id">Stable identifier, e.g. <c>animals</c>.</param>
/// <param name="Icon">Emoji shown on the pack tile.</param>
/// <param name="Locked">True when the pack is not yet available to the player.</param>
/// <param name="IsWildcard">
/// True for the "surprise" pack, which owns no pictures of its own and instead means
/// "draw from every unlocked pack".
/// </param>
public sealed record PackDefinition(string Id, string Icon, bool Locked, bool IsWildcard);
