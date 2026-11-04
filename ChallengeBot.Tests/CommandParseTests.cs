using ChallengeBot.Handlers;

namespace ChallengeBot.Tests;

public class CommandParseTests
{
    [Fact]
    public void SimpleCommand()
    {
        var result = CommandHandler.Parse("/join", "my_bot");
        Assert.Equal(("join", ""), result);
    }

    [Fact]
    public void CommandWithArgs()
    {
        var result = CommandHandler.Parse("/mark @user 2026-11-09 д", "my_bot");
        Assert.Equal(("mark", "@user 2026-11-09 д"), result);
    }

    [Fact]
    public void CommandAddressedToThisBot()
    {
        var result = CommandHandler.Parse("/status@My_Bot", "my_bot");
        Assert.Equal(("status", ""), result);
    }

    [Fact]
    public void CommandAddressedToOtherBot_Ignored() =>
        Assert.Null(CommandHandler.Parse("/status@other_bot", "my_bot"));

    [Theory]
    [InlineData("обычный текст")]
    [InlineData("")]
    [InlineData(null)]
    public void NotACommand(string? text) =>
        Assert.Null(CommandHandler.Parse(text, "my_bot"));

    [Fact]
    public void CommandIsLowercased()
    {
        var result = CommandHandler.Parse("/JOIN", "my_bot");
        Assert.Equal("join", result!.Value.Command);
    }
}
