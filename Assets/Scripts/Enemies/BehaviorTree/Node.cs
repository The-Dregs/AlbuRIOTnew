using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlbuRIOT.AI.BehaviorTree
{
    public enum NodeState
    {
        Success,
        Failure,
        Running
    }

    // Simple data bag shared across nodes
    [Serializable]
    public class Blackboard
    {
        public GameObject owner;
        public Dictionary<string, object> data = new Dictionary<string, object>();

        public T Get<T>(string key)
        {
            if (data.TryGetValue(key, out var o) && o is T t) return t;
            return default;
        }

        public void Set(string key, object value)
        {
            data[key] = value;
        }

        public bool Has(string key) => data.ContainsKey(key);
        public void Remove(string key) { if (data.ContainsKey(key)) data.Remove(key); }
    }

    public abstract class Node
    {
        public string name;
        protected Blackboard blackboard;
        protected bool started = false;

        public Node(Blackboard bb, string name = null)
        {
            this.blackboard = bb;
            this.name = name ?? GetType().Name;
        }

        public NodeState Tick()
        {
            if (!started)
            {
                OnStart();
                started = true;
            }
            var result = OnTick();
            if (result != NodeState.Running)
            {
                OnStop();
                started = false;
            }
            return result;
        }

        protected virtual void OnStart() { }
        protected virtual void OnStop() { }
        protected abstract NodeState OnTick();
    }
}
