using BirraPoint.Api.Features.Import;

namespace BirraPoint.Api.UnitTests.Import;

/// <summary>T031: blind-code generation is collision-safe within a competition-scoped batch.</summary>
public sealed class BlindCodeGeneratorTests
{
    [Fact]
    public void Generated_codes_never_collide_with_pre_existing_or_previously_generated_codes()
    {
        var existing = new HashSet<string> { "PREEXIST" };
        var random = new Random(12345);
        var generated = new List<string>();

        for (var i = 0; i < 500; i++)
        {
            generated.Add(BlindCodeGenerator.GenerateUnique(existing, random));
        }

        Assert.Equal(generated.Count, generated.Distinct().Count());
        Assert.DoesNotContain("PREEXIST", generated);
        // Every generated code is recorded into the shared set so the next call can't repeat it.
        Assert.All(generated, code => Assert.Contains(code, existing));
    }

    [Fact]
    public void Generated_code_is_non_empty_alphanumeric_and_fits_the_BlindCode_column()
    {
        var existing = new HashSet<string>();
        var random = new Random(1);

        var code = BlindCodeGenerator.GenerateUnique(existing, random);

        Assert.False(string.IsNullOrWhiteSpace(code));
        Assert.True(code.Length is > 0 and <= 10); // BeerEntry.BlindCode is string(10), data-model.md
        Assert.All(code, c => Assert.True(char.IsLetterOrDigit(c)));
    }
}
