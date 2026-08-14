namespace GPTDeskTop.Services;
public sealed record ExternalDependencyReference(ExternalDependencyType Kind, string Repository, string Identifier, string? Url);
