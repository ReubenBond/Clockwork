using Clockwork.Runtime.Scheduling;

namespace Clockwork.Runtime.Tests.Scheduling;

/// <summary>
/// Exhaustive coverage of the <see cref="ControlledOperation"/> state machine's legality table:
/// every legal edge is accepted and every one of the remaining edges is rejected. This is a pure
/// table test with no threads, so it pins the contract independently of the scheduler mechanics.
/// </summary>
public sealed class ControlledOperationStateMachineTests
{
    private static readonly ControlledOperationState[] AllStates = Enum.GetValues<ControlledOperationState>();

    private static readonly (ControlledOperationState From, ControlledOperationState To)[] LegalEdges =
    [
        (ControlledOperationState.Created, ControlledOperationState.Runnable),
        (ControlledOperationState.Created, ControlledOperationState.Canceled),
        (ControlledOperationState.Runnable, ControlledOperationState.Running),
        (ControlledOperationState.Runnable, ControlledOperationState.Canceled),
        (ControlledOperationState.Running, ControlledOperationState.Paused),
        (ControlledOperationState.Running, ControlledOperationState.Runnable),
        (ControlledOperationState.Running, ControlledOperationState.Completed),
        (ControlledOperationState.Running, ControlledOperationState.Faulted),
        (ControlledOperationState.Running, ControlledOperationState.Canceled),
        (ControlledOperationState.Paused, ControlledOperationState.Runnable),
        (ControlledOperationState.Paused, ControlledOperationState.Canceled),
    ];

    [Fact]
    public void EveryLegalEdgeIsPermitted()
    {
        foreach (var (from, to) in LegalEdges)
        {
            Assert.True(ControlledOperation.CanTransition(from, to), $"Expected {from} -> {to} to be legal.");
        }
    }

    [Fact]
    public void EveryEdgeNotInTheLegalSetIsRejected()
    {
        var legal = new HashSet<(ControlledOperationState, ControlledOperationState)>(LegalEdges);
        foreach (var from in AllStates)
        {
            foreach (var to in AllStates)
            {
                if (legal.Contains((from, to)))
                {
                    continue;
                }

                Assert.False(ControlledOperation.CanTransition(from, to), $"Expected {from} -> {to} to be illegal.");
            }
        }
    }

    [Fact]
    public void SelfTransitionsAreAlwaysIllegal()
    {
        foreach (var state in AllStates)
        {
            Assert.False(ControlledOperation.CanTransition(state, state), $"Expected {state} -> {state} to be illegal.");
        }
    }

    [Theory]
    [InlineData(ControlledOperationState.Completed)]
    [InlineData(ControlledOperationState.Faulted)]
    [InlineData(ControlledOperationState.Canceled)]
    public void TerminalStatesHaveNoOutgoingEdges(ControlledOperationState terminal)
    {
        Assert.True(ControlledOperation.IsTerminalState(terminal));
        foreach (var to in AllStates)
        {
            Assert.False(ControlledOperation.CanTransition(terminal, to), $"Terminal {terminal} must not transition to {to}.");
        }
    }

    [Theory]
    [InlineData(ControlledOperationState.Created)]
    [InlineData(ControlledOperationState.Runnable)]
    [InlineData(ControlledOperationState.Running)]
    [InlineData(ControlledOperationState.Paused)]
    public void NonTerminalStatesAreNotTerminal(ControlledOperationState state)
    {
        Assert.False(ControlledOperation.IsTerminalState(state));
    }
}
