using System;
using System.Collections.Generic;
using System.Linq;

namespace SimpleMesh.Convex;

class Graph
{
    private readonly Dictionary<int, HashSet<int>> _adjacencyList;

    public Graph()
    {
        _adjacencyList = new Dictionary<int, HashSet<int>>();
    }

    // Add an edge between two nodes
    public void AddEdge(int u, int v)
    {
        if (!_adjacencyList.ContainsKey(u))
            _adjacencyList[u] = new HashSet<int>();

        if (!_adjacencyList.ContainsKey(v))
            _adjacencyList[v] = new HashSet<int>();

        _adjacencyList[u].Add(v);
        _adjacencyList[v].Add(u);
    }

    // Create a graph from an edge list
    public static Graph FromEdgelist(IEnumerable<Point2<int>> edges)
    {
        var graph = new Graph();
        foreach (var (u, v) in edges)
        {
            graph.AddEdge(u, v);
        }
        return graph;
    }

    // Get all connected components as a list of sets
    public List<HashSet<int>> ConnectedComponents()
    {
        var visited = new HashSet<int>();
        var components = new List<HashSet<int>>();

        foreach (var node in _adjacencyList.Keys)
        {
            if (!visited.Contains(node))
            {
                var component = new HashSet<int>();
                Explore(node, visited, component);
                components.Add(component);
            }
        }

        return components;
    }

    private void Explore(int node, HashSet<int> visited, HashSet<int> component)
    {
        var stack = new Stack<int>();
        stack.Push(node);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            if (!visited.Contains(current))
            {
                visited.Add(current);
                component.Add(current);

                foreach (var neighbor in _adjacencyList[current])
                {
                    if (!visited.Contains(neighbor))
                        stack.Push(neighbor);
                }
            }
        }
    }

    // Get a subgraph containing only the specified nodes
    public Graph Subgraph(IEnumerable<int> nodes)
    {
        var subgraph = new Graph();
        var nodeSet = new HashSet<int>(nodes);

        foreach (var node in nodeSet)
        {
            if (_adjacencyList.ContainsKey(node))
            {
                foreach (var neighbor in _adjacencyList[node])
                {
                    if (nodeSet.Contains(neighbor))
                        subgraph.AddEdge(node, neighbor);
                }
            }
        }

        return subgraph;
    }

    // Perform BFS and yield edges in BFS order
    public IEnumerable<Point2<int>> BfsEdges(int start)
    {
        var visited = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var neighbor in _adjacencyList[current])
            {
                if (!visited.Contains(neighbor))
                {
                    visited.Add(neighbor);
                    queue.Enqueue(neighbor);
                    yield return new(current, neighbor);
                }
            }
        }
    }

    // Get all nodes in the graph
    public IEnumerable<int> Nodes()
    {
        return _adjacencyList.Keys;
    }

    public override string ToString()
    {
        return string.Join("\n", _adjacencyList.Select(kvp => $"{kvp.Key}: [{string.Join(", ", kvp.Value)}]"));
    }
}