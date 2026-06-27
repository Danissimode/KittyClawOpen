using System;
using KittyClaw.Core.Automation.Runners;
using Xunit;

namespace KittyClaw.Core.Tests.Automation.Runners;

public class ExecutionModeTests
{
    [Fact]
    public void ExecutionMode_EnumValues_AreCorrect()
    {
        var allModes = Enum.GetValues<ExecutionMode>();
        
        Assert.Contains(ExecutionMode.LegacyClaude, allModes);
        Assert.Contains(ExecutionMode.DirectOpenCode, allModes);
        Assert.Contains(ExecutionMode.CaoGoverned, allModes);
        Assert.Contains(ExecutionMode.TeamWorkflow, allModes);
        Assert.Contains(ExecutionMode.Manual, allModes);
    }
    
    [Fact]
    public void ExecutionMode_DefaultValue_IsLegacyClaude()
    {
        var defaultMode = default(ExecutionMode);
        Assert.Equal(ExecutionMode.LegacyClaude, defaultMode);
    }
    
    [Fact]
    public void ExecutionMode_ToString_ReturnsCorrectValues()
    {
        Assert.Equal("LegacyClaude", ExecutionMode.LegacyClaude.ToString());
        Assert.Equal("DirectOpenCode", ExecutionMode.DirectOpenCode.ToString());
        Assert.Equal("CaoGoverned", ExecutionMode.CaoGoverned.ToString());
        Assert.Equal("TeamWorkflow", ExecutionMode.TeamWorkflow.ToString());
        Assert.Equal("Manual", ExecutionMode.Manual.ToString());
    }
    
    [Fact]
    public void ExecutionMode_CanBeCastToInt()
    {
        var legacy = (int)ExecutionMode.LegacyClaude;
        var direct = (int)ExecutionMode.DirectOpenCode;
        var cao = (int)ExecutionMode.CaoGoverned;
        var team = (int)ExecutionMode.TeamWorkflow;
        var manual = (int)ExecutionMode.Manual;
        
        // Values should be sequential starting from 0
        Assert.Equal(0, legacy);
        Assert.Equal(1, direct);
        Assert.Equal(2, cao);
        Assert.Equal(3, team);
        Assert.Equal(4, manual);
    }
}
