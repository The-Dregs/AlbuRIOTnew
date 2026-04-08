using System.Collections.Generic;

namespace AlbuRIOT.AI.BehaviorTree
{
    public abstract class Composite : Node
    {
        protected readonly List<Node> children = new List<Node>();
        public Composite(Blackboard bb, string name = null) : base(bb, name) { }
        public Composite Add(params Node[] nodes) { children.AddRange(nodes); return this; }
    }

    // Runs children left-to-right until one fails or runs
    public class Sequence : Composite
    {
        private int currentIndex;
        public Sequence(Blackboard bb, string name = null) : base(bb, name) { }
        protected override void OnStart() { currentIndex = 0; }
        protected override NodeState OnTick()
        {
            while (currentIndex < children.Count)
            {
                var state = children[currentIndex].Tick();
                switch (state)
                {
                    case NodeState.Running:
                        return NodeState.Running;
                    case NodeState.Failure:
                        return NodeState.Failure;
                    case NodeState.Success:
                        currentIndex++;
                        continue;
                }
            }
            return NodeState.Success;
        }
    }

    // Runs children until one succeeds or runs
    public class Selector : Composite
    {
        public Selector(Blackboard bb, string name = null) : base(bb, name) { }
        // Stateless selector: always re-evaluate from first child each tick so higher-priority checks (like target detection)
        // can interrupt lower-priority ones (like patrol).
        protected override void OnStart() { }
        protected override NodeState OnTick()
        {
            for (int i = 0; i < children.Count; i++)
            {
                var state = children[i].Tick();
                if (state == NodeState.Success) return NodeState.Success;
                if (state == NodeState.Running) return NodeState.Running;
                // on Failure try next child
            }
            return NodeState.Failure;
        }
    }

    // Stateful selector: remembers which child was running and continues with it
    public class StatefulSelector : Composite
    {
        private int currentIndex;
        public StatefulSelector(Blackboard bb, string name = null) : base(bb, name) { }
        protected override void OnStart() { currentIndex = 0; }
        protected override NodeState OnTick()
        {
            // If we have a running child, continue with it
            if (currentIndex < children.Count)
            {
                var state = children[currentIndex].Tick();
                if (state == NodeState.Running) return NodeState.Running;
                if (state == NodeState.Success) return NodeState.Success;
                // on Failure, try next child
                currentIndex++;
            }
            
            // Try remaining children
            while (currentIndex < children.Count)
            {
                var state = children[currentIndex].Tick();
                if (state == NodeState.Success) return NodeState.Success;
                if (state == NodeState.Running) return NodeState.Running;
                currentIndex++;
            }
            return NodeState.Failure;
        }
    }
}
