using System;

namespace AlbuRIOT.AI.BehaviorTree
{
    // Leaf: run an action delegate
    public class ActionNode : Node
    {
        private readonly Func<NodeState> action;
        public ActionNode(Blackboard bb, Func<NodeState> action, string name = null) : base(bb, name)
        {
            this.action = action;
        }
        protected override NodeState OnTick() => action != null ? action() : NodeState.Failure;
    }

    // Condition leaf
    public class ConditionNode : Node
    {
        private readonly Func<bool> predicate;
        public ConditionNode(Blackboard bb, Func<bool> predicate, string name = null) : base(bb, name) { this.predicate = predicate; }
        protected override NodeState OnTick() => predicate != null && predicate() ? NodeState.Success : NodeState.Failure;
    }
}
