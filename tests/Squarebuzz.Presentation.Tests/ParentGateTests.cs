using System.Globalization;
using Squarebuzz.Presentation.Tests.Fakes;
using Squarebuzz.Presentation.ViewModels;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class ParentGateTests
{
    private readonly ParentGate _gate = new(new FakeLocalizationService());

    /// <summary>The gate's question is "a × b = ?"; an adult solves it, and so does a test.</summary>
    private string CorrectAnswer()
    {
        var parts = _gate.Question.Split('×');
        var left = int.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
        var right = int.Parse(parts[1].Replace("= ?", "", StringComparison.Ordinal).Trim(), CultureInfo.InvariantCulture);

        return (left * right).ToString(CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Open_PosesAQuestion_AndShowsTheGate()
    {
        _gate.Open(() => Task.CompletedTask);

        Assert.True(_gate.IsOpen);
        Assert.Matches(@"^\d × \d = \?$", _gate.Question);
        Assert.False(_gate.HasFailed);
        Assert.Equal(string.Empty, _gate.Answer);
    }

    [Fact]
    public async Task CorrectAnswer_ClosesTheGate_AndRunsTheCallback()
    {
        var passed = false;
        _gate.Open(() =>
        {
            passed = true;
            return Task.CompletedTask;
        });

        _gate.Answer = CorrectAnswer();
        await _gate.SubmitCommand.ExecuteAsync(null);

        Assert.False(_gate.IsOpen);
        Assert.True(passed);
    }

    [Fact]
    public async Task WrongAnswer_FailsVisibly_AndWithholdsTheCallback()
    {
        var passed = false;
        _gate.Open(() =>
        {
            passed = true;
            return Task.CompletedTask;
        });

        _gate.Answer = "1";
        await _gate.SubmitCommand.ExecuteAsync(null);

        Assert.True(_gate.IsOpen);
        Assert.True(_gate.HasFailed);
        Assert.Equal(string.Empty, _gate.Answer);
        Assert.False(passed);
    }

    [Fact]
    public async Task WrongThenRightAnswer_StillPasses()
    {
        var passed = false;
        _gate.Open(() =>
        {
            passed = true;
            return Task.CompletedTask;
        });

        _gate.Answer = "1";
        await _gate.SubmitCommand.ExecuteAsync(null);

        _gate.Answer = CorrectAnswer();
        await _gate.SubmitCommand.ExecuteAsync(null);

        Assert.False(_gate.IsOpen);
        Assert.True(passed);
    }

    [Fact]
    public async Task Cancel_ClosesTheGate_AndForgetsTheCallback()
    {
        var passed = false;
        _gate.Open(() =>
        {
            passed = true;
            return Task.CompletedTask;
        });

        var answer = CorrectAnswer();
        _gate.CancelCommand.Execute(null);

        Assert.False(_gate.IsOpen);

        // Even the right answer after a cancel must not run the callback.
        _gate.Answer = answer;
        await _gate.SubmitCommand.ExecuteAsync(null);
        Assert.False(passed);
    }

    [Fact]
    public void EachOpening_PosesAFreshState()
    {
        _gate.Open(() => Task.CompletedTask);
        _gate.Answer = "junk";
        _gate.CancelCommand.Execute(null);

        _gate.Open(() => Task.CompletedTask);

        Assert.True(_gate.IsOpen);
        Assert.False(_gate.HasFailed);
        Assert.Equal(string.Empty, _gate.Answer);
    }
}
