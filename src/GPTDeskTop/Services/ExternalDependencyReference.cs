namespace GPTDeskTop.Services;
public sealed record ExternalDependencyReference(ExternalDependencyKind Kind, string Repository, string Identifier, string? Url);
