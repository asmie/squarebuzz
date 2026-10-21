using Squarebuzz.Presentation.Tests.Fakes;
using Squarebuzz.Presentation.ViewModels;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public class HowToViewModelTests
{
    [Fact]
    public void OverlapDiagram_ShowsBothEightCellPlacementsAndTheirIntersection()
    {
        using var vm = new HowToViewModel(new FakeLocalizationService());
        var section = vm.Sections[2];
        var left = section.Rows[0];
        var right = section.Rows[1];
        var result = section.Rows[2];

        Assert.Equal(10, left.Length);
        Assert.Equal(8, left.Count(c => c is '#' or 'o'));
        Assert.Equal(8, right.Count(c => c is '#' or 'o'));
        Assert.Equal(6, result.Count(c => c == 'o'));
        for (var i = 0; i < left.Length; i++)
        {
            Assert.Equal(left[i] != '.' && right[i] != '.', result[i] == 'o');
        }
    }
}
