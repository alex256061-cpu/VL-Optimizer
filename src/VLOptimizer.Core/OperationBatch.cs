namespace VLOptimizer.Core;

public static class OperationBatch
{
    /// <summary>Do not offer contradictory or overlapping snapshots as one approval.
    /// Unrelated original previews retain their instance-bound authorization.</summary>
    public static IReadOnlyList<OperationPreview> PreventOverlaps(IReadOnlyList<OperationPreview> previews)
    {
        var collisions = previews.Where(p => p.CanApply)
            .SelectMany(p => p.Resources.Distinct(StringComparer.OrdinalIgnoreCase).Select(r => (Resource: r, Preview: p)))
            .GroupBy(x => x.Resource, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(x => x.Preview)).ToHashSet(ReferenceEqualityComparer.Instance);
        return previews.Select(p => collisions.Contains(p) ? p with { CanApply = false,
            BlockReason = "Esta seleção contém outra operação que altera o mesmo recurso. Revise uma delas por vez para preservar o antes/depois aprovado." } : p).ToArray();
    }
}
