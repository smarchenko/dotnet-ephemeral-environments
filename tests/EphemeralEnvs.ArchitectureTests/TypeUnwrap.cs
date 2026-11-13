namespace EphemeralEnvs.ArchitectureTests;

internal static class TypeUnwrap
{
    /// <summary>
    /// The type itself plus everything inside arrays and generic arguments:
    /// <c>Task&lt;ActionResult&lt;ProductDto[]&gt;&gt;</c> → Task, ActionResult, ProductDto.
    /// An array reports its element's assembly, so only the element is returned (ProductDto, not ProductDto[]).
    /// </summary>
    public static IEnumerable<Type> All(Type type)
    {
        if (!type.IsArray)
        {
            yield return type;
        }

        var inner = type.IsArray ? [type.GetElementType()!] : type.GetGenericArguments();
        foreach (var nested in inner.SelectMany(All))
        {
            yield return nested;
        }
    }
}
