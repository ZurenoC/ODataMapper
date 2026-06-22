using ODataMapper.Models;

namespace ODataMapper.Services;

/// <summary>
/// Scoped state container that persists metadata for the lifetime of a Blazor Server circuit.
/// Injected into all pages so navigation does not lose the loaded data.
/// </summary>
public class MetadataStateService
{
    public ODataMetadataResult? CurrentMetadata { get; private set; }
    public bool IsLoaded => CurrentMetadata != null;
    public string? LastUrl { get; private set; }

    public event Action? OnMetadataChanged;

    public void SetMetadata(ODataMetadataResult metadata, string url)
    {
        CurrentMetadata = metadata;
        LastUrl = url;
        OnMetadataChanged?.Invoke();
    }

    public void Clear()
    {
        CurrentMetadata = null;
        LastUrl = null;
        OnMetadataChanged?.Invoke();
    }

    /// <summary>
    /// Find an entity by its full type name or short name.
    /// </summary>
    public ODataEntity? FindEntity(string typeName)
    {
        if (CurrentMetadata == null) return null;
        return CurrentMetadata.Entities.FirstOrDefault(e => e.FullTypeName == typeName)
            ?? CurrentMetadata.Entities.FirstOrDefault(e => e.Name == typeName);
    }

    /// <summary>
    /// Find an entity by its EntitySet name.
    /// </summary>
    public ODataEntity? FindEntityBySet(string entitySetName)
    {
        return CurrentMetadata?.Entities.FirstOrDefault(e => e.EntitySetName == entitySetName);
    }

    /// <summary>
    /// Find an enum type by its full name or short name.
    /// </summary>
    public ODataEnumType? FindEnum(string enumTypeName)
    {
        if (CurrentMetadata == null) return null;
        return CurrentMetadata.EnumTypes.FirstOrDefault(e => e.FullName == enumTypeName)
            ?? CurrentMetadata.EnumTypes.FirstOrDefault(e => e.Name == enumTypeName);
    }
}
