namespace ODataMapper.Models;

/// <summary>
/// Represents a unified OData entity combining EntitySet and EntityType information.
/// </summary>
public class ODataEntity
{
    public string Name { get; set; } = string.Empty;
    public string EntitySetName { get; set; } = string.Empty;
    public string EntitySetUrl { get; set; } = string.Empty;
    public string EntityTypeName { get; set; } = string.Empty;
    public string FullTypeName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string? BaseType { get; set; }
    public List<ODataProperty> Properties { get; set; } = [];
    public List<ODataProperty> KeyProperties { get; set; } = [];
    public List<ODataNavigation> NavigationProperties { get; set; } = [];
    public bool HasMultipleVersions { get; set; }
    public List<string> RelatedVersions { get; set; } = [];
}

/// <summary>
/// Represents a property/field on an OData entity.
/// </summary>
public class ODataProperty
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string DisplayType { get; set; } = string.Empty;
    public bool IsNullable { get; set; } = true;
    public bool IsKey { get; set; }
    public bool IsEnum { get; set; }
    public string? EnumTypeName { get; set; }
    public int? MaxLength { get; set; }
}

/// <summary>
/// Represents a navigation property (relationship) on an OData entity.
/// </summary>
public class ODataNavigation
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string TargetEntityName { get; set; } = string.Empty;
    public bool IsCollection { get; set; }
    public string RelationshipType => IsCollection ? "One-to-Many" : "Many-to-One";
    public string? Partner { get; set; }
}

/// <summary>
/// Represents an OData enum type with its members.
/// </summary>
public class ODataEnumType
{
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string UnderlyingType { get; set; } = "Edm.Int32";
    public bool IsFlags { get; set; }
    public List<ODataEnumMember> Members { get; set; } = [];
}

/// <summary>
/// Represents a single member/value of an enum type.
/// </summary>
public class ODataEnumMember
{
    public string Name { get; set; } = string.Empty;
    public string? Value { get; set; }
}

/// <summary>
/// Holds the complete parsed metadata result.
/// </summary>
public class ODataMetadataResult
{
    public string SourceUrl { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public DateTime LoadedAt { get; set; } = DateTime.UtcNow;
    public List<ODataEntity> Entities { get; set; } = [];
    public List<ODataEnumType> EnumTypes { get; set; } = [];
    public int TotalEntitySets { get; set; }
    public int TotalEntityTypes { get; set; }
    public int TotalEnumTypes { get; set; }
}
