using System.Globalization;
using Squarebuzz.Presentation.Tests.Fakes;
using Squarebuzz.Presentation.ViewModels;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class ParentGateTests
{
    private readonly ParentGate _gate = new(new FakeLocalizationService());

    /// <summary>The bidi isolate the question is wrapped in, so it reads left to right on an RTL page.</summary>
    private const string Lri = "⁦";
    private const string Pdi = "⁩";

    /// <summary>The gate's question is "a × b = ?"; an adult solves it, and so does a test.</summary>
    private string CorrectAnswer()
    {
        var parts = _gate.Question.Trim(Lri[0], Pdi[0]).Split('×');
        var left = int.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
        var right = int.Parse(parts[1].Replace("= ?", "", StringComparison.Ordinal).Trim(), CultureInfo.InvariantCulture);

        return (left * right).ToString(CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Open_PosesAQuestion_AndShowsTheGate()
    {
        _gate.Open(() => Task.CompletedTask);

        Assert.True(_gate.IsOpen);

        // Wrapped in an isolate so the sum reads left to right on an Arabic, Hebrew or Persian
        // page, where digits are weak and "×", "=" and "?" carry no direction of their own.
        Assert.Matches($@"^{Lri}\d × \d = \?{Pdi}$", _gate.Question);
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
    public async Task AnswerWithSurroundingWhitespace_IsAccepted()
    {
        var passed = false;
        _gate.Open(() =>
        {
            passed = true;
            return Task.CompletedTask;
        });

        _gate.Answer = $"  {CorrectAnswer()}\t";
        await _gate.SubmitCommand.ExecuteAsync(null);

        Assert.True(passed);
    }

    [Fact]
    // A Persian or Bengali keyboard produces its own digits by default. A parent typing the right
    // answer in their own numerals must not be told to ask a grown-up.
    public async Task AnswerInNativeDigits_IsAccepted()
    {
        var passed = false;
        _gate.Open(() =>
        {
            passed = true;
            return Task.CompletedTask;
        });

        var persian = string.Concat(CorrectAnswer().Select(c => (char)('۰' + (c - '0'))));
        _gate.Answer = persian;
        await _gate.SubmitCommand.ExecuteAsync(null);

        Assert.True(passed);
    }

    [Fact]
    // The plain int.TryParse(string) parses under the current culture, where a leading sign or a
    // group separator is legal - so a decorated right answer passed in one language setting and
    // failed in the next. The gate's strictness must not depend on the language: only the bare
    // digits of the answer count, however they are dressed up.
    public async Task TheRightAnswerDressedUp_IsStillRejected()
    {
        var passed = false;
        _gate.Open(() =>
        {
            passed = true;
            return Task.CompletedTask;
        });

        var correct = CorrectAnswer();
        string[] decorated =
        [
            $"+{correct}",
            $"{correct}.0",
            $"{correct}x",
            $"{correct[0]},{correct[1..]}",
            $"{correct[0]} {correct[1..]}",
        ];

        foreach (var answer in decorated)
        {
            _gate.Answer = answer;
            await _gate.SubmitCommand.ExecuteAsync(null);

            Assert.False(passed, $"\"{answer}\" was accepted as {correct}");
            Assert.True(_gate.HasFailed);
        }
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
