using System.Xml.Linq;
using ODataMapper.Models;

namespace ODataMapper.Services;

public class ODataMetadataService
{
    private readonly HttpClient _httpClient;

    public ODataMetadataService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Fetches and parses OData metadata from a given base URL.
    /// Automatically appends /data/$metadata if not already present.
    /// </summary>
    public async Task<ODataMetadataResult> LoadMetadataAsync(string url)
    {
        var metadataUrl = NormalizeUrl(url);
        var baseUrl = ExtractBaseUrl(url);

        using var response = await _httpClient.GetAsync(metadataUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        var doc = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);

        return ParseMetadata(doc, baseUrl, metadataUrl);
    }

    /// <summary>
    /// Parses metadata from an XML string (for testing or file upload scenarios).
    /// </summary>
    public ODataMetadataResult ParseMetadataFromXml(string xml, string baseUrl)
    {
        var doc = XDocument.Parse(xml);
        return ParseMetadata(doc, baseUrl, baseUrl + "/data/$metadata");
    }

    private ODataMetadataResult ParseMetadata(XDocument doc, string baseUrl, string sourceUrl)
    {
        var result = new ODataMetadataResult
        {
            SourceUrl = sourceUrl,
            BaseUrl = baseUrl,
            LoadedAt = DateTime.UtcNow
        };

        var edmxNs = doc.Root?.Name.Namespace ?? XNamespace.None;
        var schemas = doc.Descendants().Where(e => e.Name.LocalName == "Schema").ToList();

        var allEntityTypes = new Dictionary<string, (XElement Element, string Namespace)>();
        var allEnumTypes = new Dictionary<string, (XElement Element, string Namespace)>();
        var allEntitySets = new List<(string Name, string EntityType)>();

        foreach (var schema in schemas)
        {
            var ns = schema.Attribute("Namespace")?.Value ?? string.Empty;

            // Parse EntityTypes
            foreach (var entityType in schema.Elements().Where(e => e.Name.LocalName == "EntityType"))
            {
                var name = entityType.Attribute("Name")?.Value ?? string.Empty;
                var fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                allEntityTypes[fullName] = (entityType, ns);
            }

            // Parse EnumTypes
            foreach (var enumType in schema.Elements().Where(e => e.Name.LocalName == "EnumType"))
            {
                var name = enumType.Attribute("Name")?.Value ?? string.Empty;
                var fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                allEnumTypes[fullName] = (enumType, ns);
            }

            // Parse EntityContainer for EntitySets
            foreach (var container in schema.Elements().Where(e => e.Name.LocalName == "EntityContainer"))
            {
                foreach (var entitySet in container.Elements().Where(e => e.Name.LocalName == "EntitySet"))
                {
                    var setName = entitySet.Attribute("Name")?.Value ?? string.Empty;
                    var entityTypeName = entitySet.Attribute("EntityType")?.Value ?? string.Empty;
                    allEntitySets.Add((setName, entityTypeName));
                }
            }
        }

        // Parse enum types
        foreach (var (fullName, (element, ns)) in allEnumTypes)
        {
            var enumType = ParseEnumType(element, ns);
            result.EnumTypes.Add(enumType);
        }

        // Build entities by combining EntitySets with their EntityTypes
        var entitySetLookup = allEntitySets
            .GroupBy(es => es.EntityType)
            .ToDictionary(g => g.Key, g => g.First().Name);

        foreach (var (fullTypeName, (element, ns)) in allEntityTypes)
        {
            var entitySetName = entitySetLookup.GetValueOrDefault(fullTypeName, string.Empty);

            var entity = ParseEntityType(element, ns, fullTypeName, entitySetName, baseUrl, allEnumTypes);
            result.Entities.Add(entity);
        }

        // Also add entities that have EntitySets but might be referenced differently
        foreach (var (setName, entityType) in allEntitySets)
        {
            if (!result.Entities.Any(e => e.EntitySetName == setName))
            {
                // Try to find the type by short name
                var matchingType = allEntityTypes.FirstOrDefault(kvp =>
                    kvp.Key == entityType || kvp.Key.EndsWith("." + entityType));

                if (matchingType.Value.Element != null)
                {
                    var entity = ParseEntityType(matchingType.Value.Element, matchingType.Value.Namespace,
                        matchingType.Key, setName, baseUrl, allEnumTypes);
                    if (!result.Entities.Any(e => e.FullTypeName == entity.FullTypeName))
                    {
                        result.Entities.Add(entity);
                    }
                }
            }
        }

        // Ensure all entities with EntitySets have their EntitySetName assigned
        foreach (var (setName, entityType) in allEntitySets)
        {
            var entity = result.Entities.FirstOrDefault(e => e.FullTypeName == entityType);
            if (entity != null && string.IsNullOrEmpty(entity.EntitySetName))
            {
                entity.EntitySetName = setName;
                entity.EntitySetUrl = $"{baseUrl}/data/{setName}";
            }
        }

        // Detect versioned entities (e.g., Projects, ProjectsV2)
        DetectVersionedEntities(result.Entities);

        result.TotalEntitySets = allEntitySets.Count;
        result.TotalEntityTypes = allEntityTypes.Count;
        result.TotalEnumTypes = allEnumTypes.Count;

        return result;
    }

    private ODataEntity ParseEntityType(XElement element, string ns, string fullTypeName,
        string entitySetName, string baseUrl, Dictionary<string, (XElement, string)> enumTypes)
    {
        var name = element.Attribute("Name")?.Value ?? string.Empty;
        var baseType = element.Attribute("BaseType")?.Value;

        var entity = new ODataEntity
        {
            Name = name,
            EntitySetName = entitySetName,
            EntitySetUrl = string.IsNullOrEmpty(entitySetName) ? string.Empty : $"{baseUrl}/data/{entitySetName}",
            EntityTypeName = name,
            FullTypeName = fullTypeName,
            Namespace = ns,
            BaseType = baseType
        };

        // Parse Key properties
        var keyElement = element.Elements().FirstOrDefault(e => e.Name.LocalName == "Key");
        var keyNames = new HashSet<string>();
        if (keyElement != null)
        {
            foreach (var propRef in keyElement.Elements().Where(e => e.Name.LocalName == "PropertyRef"))
            {
                var keyName = propRef.Attribute("Name")?.Value;
                if (keyName != null) keyNames.Add(keyName);
            }
        }

        // Parse Properties
        foreach (var prop in element.Elements().Where(e => e.Name.LocalName == "Property"))
        {
            var property = ParseProperty(prop, keyNames, enumTypes);
            entity.Properties.Add(property);
            if (property.IsKey)
            {
                entity.KeyProperties.Add(property);
            }
        }

        // Parse Navigation Properties
        foreach (var nav in element.Elements().Where(e => e.Name.LocalName == "NavigationProperty"))
        {
            var navigation = ParseNavigationProperty(nav);
            entity.NavigationProperties.Add(navigation);
        }

        return entity;
    }

    private ODataProperty ParseProperty(XElement element, HashSet<string> keyNames,
        Dictionary<string, (XElement, string)> enumTypes)
    {
        var name = element.Attribute("Name")?.Value ?? string.Empty;
        var type = element.Attribute("Type")?.Value ?? "Edm.String";
        var nullable = element.Attribute("Nullable")?.Value;
        var maxLength = element.Attribute("MaxLength")?.Value;

        var isEnum = enumTypes.ContainsKey(type);
        var displayType = GetDisplayType(type);

        return new ODataProperty
        {
            Name = name,
            Type = type,
            DisplayType = displayType,
            IsNullable = nullable != "false",
            IsKey = keyNames.Contains(name),
            IsEnum = isEnum,
            EnumTypeName = isEnum ? type : null,
            MaxLength = int.TryParse(maxLength, out var ml) ? ml : null
        };
    }

    private ODataNavigation ParseNavigationProperty(XElement element)
    {
        var name = element.Attribute("Name")?.Value ?? string.Empty;
        var type = element.Attribute("Type")?.Value ?? string.Empty;
        var partner = element.Attribute("Partner")?.Value;

        var isCollection = type.StartsWith("Collection(");
        var targetType = isCollection
            ? type["Collection(".Length..^1]
            : type;

        // Extract just the entity name from the full type
        var targetEntityName = targetType.Contains('.')
            ? targetType[(targetType.LastIndexOf('.') + 1)..]
            : targetType;

        return new ODataNavigation
        {
            Name = name,
            Type = type,
            TargetEntityName = targetEntityName,
            IsCollection = isCollection,
            Partner = partner
        };
    }

    private ODataEnumType ParseEnumType(XElement element, string ns)
    {
        var name = element.Attribute("Name")?.Value ?? string.Empty;
        var underlyingType = element.Attribute("UnderlyingType")?.Value ?? "Edm.Int32";
        var isFlags = element.Attribute("IsFlags")?.Value == "true";

        var enumType = new ODataEnumType
        {
            Name = name,
            FullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}",
            UnderlyingType = underlyingType,
            IsFlags = isFlags
        };

        foreach (var member in element.Elements().Where(e => e.Name.LocalName == "Member"))
        {
            enumType.Members.Add(new ODataEnumMember
            {
                Name = member.Attribute("Name")?.Value ?? string.Empty,
                Value = member.Attribute("Value")?.Value
            });
        }

        return enumType;
    }

    private static void DetectVersionedEntities(List<ODataEntity> entities)
    {
        // Group entities by base name (strip V2, V3, etc. suffix)
        var groups = entities
            .Where(e => !string.IsNullOrEmpty(e.EntitySetName))
            .GroupBy(e => GetBaseName(e.EntitySetName))
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in groups)
        {
            var versions = group.Select(e => e.EntitySetName).ToList();
            foreach (var entity in group)
            {
                entity.HasMultipleVersions = true;
                entity.RelatedVersions = versions.Where(v => v != entity.EntitySetName).ToList();
            }
        }
    }

    private static string GetBaseName(string name)
    {
        // Remove V2, V3, V4 etc. suffixes for grouping
        if (name.Length > 2)
        {
            for (int i = name.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(name[i])) continue;
                if (name[i] == 'V' && i < name.Length - 1)
                {
                    return name[..i];
                }
                break;
            }
        }
        return name;
    }

    private static string GetDisplayType(string type)
    {
        if (type.StartsWith("Edm."))
            return type[4..];
        if (type.StartsWith("Collection("))
            return $"Collection({GetDisplayType(type["Collection(".Length..^1])})";
        if (type.Contains('.'))
            return type[(type.LastIndexOf('.') + 1)..];
        return type;
    }

    private static string NormalizeUrl(string url)
    {
        url = url.Trim().TrimEnd('/');

        if (url.EndsWith("/$metadata", StringComparison.OrdinalIgnoreCase) ||
            url.EndsWith("/$metadata/", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        if (url.EndsWith("/data", StringComparison.OrdinalIgnoreCase))
        {
            return url + "/$metadata";
        }

        return url + "/data/$metadata";
    }

    private static string ExtractBaseUrl(string url)
    {
        url = url.Trim().TrimEnd('/');

        var dataIndex = url.IndexOf("/data", StringComparison.OrdinalIgnoreCase);
        if (dataIndex > 0)
        {
            return url[..dataIndex];
        }

        return url;
    }
}
