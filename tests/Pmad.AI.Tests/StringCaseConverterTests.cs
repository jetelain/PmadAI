using Pmad.AI.Common;

namespace Pmad.AI.Tests;

/// <summary>
/// Unit tests for <see cref="StringCaseConverter"/>, covering the snake_case, camelCase, and
/// PascalCase conversions used by lenient tool dispatch to normalize parameter and enum spellings.
/// </summary>
public class StringCaseConverterTests
{
    [Theory]
    [InlineData("wild tiger", "wild_tiger")]
    [InlineData("wild   tiger", "wild_tiger")]
    [InlineData("wild  _  tiger", "wild_tiger")]
    [InlineData("wild-tiger", "wild_tiger")]
    [InlineData("filePath", "file_path")]
    [InlineData("FilePath", "file_path")]
    [InlineData("oldText", "old_text")]
    [InlineData("HTTPResponse", "http_response")]
    [InlineData("already_snake", "already_snake")]
    [InlineData("A", "a")]
    [InlineData("", "")]
    public void ToSnakeCase_ConvertsToSnakeCase(string input, string expected)
    {
        Assert.Equal(expected, StringCaseConverter.ToSnakeCase(input));
    }

    [Theory]
    [InlineData("wild tiger", "wildTiger")]
    [InlineData("wild   tiger", "wildTiger")]
    [InlineData("wild  _  tiger", "wildTiger")]
    [InlineData("wild-tiger", "wildTiger")]
    [InlineData("file_path", "filePath")]
    [InlineData("filePath", "filePath")]
    [InlineData("FilePath", "filePath")]
    [InlineData("a", "a")]
    [InlineData("", "")]
    public void ToCamelCase_ConvertsToCamelCase(string input, string expected)
    {
        Assert.Equal(expected, StringCaseConverter.ToCamelCase(input));
    }

    [Theory]
    [InlineData("wild tiger", "WildTiger")]
    [InlineData("wild   tiger", "WildTiger")]
    [InlineData("wild  _  tiger", "WildTiger")]
    [InlineData("wild-tiger", "WildTiger")]
    [InlineData("file_path", "FilePath")]
    [InlineData("filePath", "FilePath")]
    [InlineData("FilePath", "FilePath")]
    [InlineData("a", "A")]
    [InlineData("", "")]
    public void ToPascalCase_ConvertsToPascalCase(string input, string expected)
    {
        Assert.Equal(expected, StringCaseConverter.ToPascalCase(input));
    }

    [Fact]
    public void RoundTrips_Snake_Camel_Pascal()
    {
        Assert.Equal("file_path", StringCaseConverter.ToSnakeCase(StringCaseConverter.ToCamelCase("file_path")));
        Assert.Equal("filePath", StringCaseConverter.ToCamelCase(StringCaseConverter.ToPascalCase("filePath")));
    }
}
