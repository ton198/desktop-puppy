namespace DesktopPuppy.Ai;

public sealed class AiPipeline(IAiContentProvider content, IJevDecisionProvider selector, IAiDelay delay)
{
    public async Task<AiOutcome> RunAsync(AiScene scene, Action<IReadOnlyList<ContentCandidate>> saveCandidates, CancellationToken cancellationToken)
    {
        var pool = new List<ContentCandidate>(); var validator = new ContentValidator(); int generation = 0, selection = 0; var error = "InsufficientCandidates";
        while (generation < 5 && pool.Count < 4)
        {
            cancellationToken.ThrowIfCancellationRequested(); generation++;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(8));
                var batch = await content.GenerateAsync(scene, pool.ToArray(), generation, timeout.Token).ConfigureAwait(false);
                if (batch.Count != 6) throw new AiProviderException("CandidateCount");
                foreach (var c in batch)
                {
                    if (validator.IsValid(c, pool, scene.Recent)) pool.Add(c with { Id = $"c{generation}_{pool.Count}" });
                }
                saveCandidates(pool);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { error = "Timeout"; }
            catch (AiProviderException e) { error = e.Kind; }
            if (pool.Count < 4 && generation < 5) await delay.WaitAsync(TimeSpan.FromSeconds(1 << (generation - 1)), cancellationToken).ConfigureAwait(false);
        }
        if (pool.Count < 4) return new(null, error, generation, 0);
        while (selection < 5)
        {
            cancellationToken.ThrowIfCancellationRequested(); selection++;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var result = await selector.SelectAsync(scene, pool, timeout.Token).ConfigureAwait(false);
                var selected = pool.FirstOrDefault(c => c.Id == result.CandidateId);
                if (selected is null || !ValidProbabilities(result, pool)) throw new AiProviderException("Decision");
                var probabilities = result.Probabilities.Values.OrderDescending().ToArray();
                if (probabilities[0] < .55 || probabilities[0] - probabilities[1] < .10) selected = ContentValidator.TieBreak(scene, pool);
                return new(selected, "", generation, selection);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { error = "Timeout"; }
            catch (AiProviderException e) { error = e.Kind; }
            if (selection < 5) await delay.WaitAsync(TimeSpan.FromSeconds(1 << (selection - 1)), cancellationToken).ConfigureAwait(false);
        }
        return new(null, error, generation, selection);
    }
    private static bool ValidProbabilities(JevDecision result, IReadOnlyList<ContentCandidate> pool) => double.IsFinite(result.Confidence) && result.Confidence is >= 0 and <= 1 &&
        result.Probabilities.Count == pool.Count && pool.All(c => result.Probabilities.TryGetValue(c.Id, out var probability) && double.IsFinite(probability) && probability is >= 0 and <= 1) && Math.Abs(result.Probabilities.Values.Sum() - 1) < .02;
}
