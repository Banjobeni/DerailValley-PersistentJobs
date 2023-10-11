using System;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using HarmonyLib;

namespace PersistentJobsMod.Utilities {
    public static class TaskUtilities {
        public static void TaskDoDfs(Task task, Action<Task> action) {
            if (task is ParallelTasks || task is SequentialTasks) {
                Traverse.Create(task)
                    .Field("tasks")
                    .GetValue<IEnumerable<Task>>()
                    .Do(t => TaskDoDfs(t, action));
            }
            action(task);
        }

        public static bool TaskAnyDfs(Task task, Func<Task, bool> predicate) {
            if (task is ParallelTasks || task is SequentialTasks) {
                return Traverse.Create(task)
                    .Field("tasks")
                    .GetValue<IEnumerable<Task>>()
                    .Any(t => TaskAnyDfs(t, predicate));
            }
            return predicate(task);
        }

        public static Task TaskFindDfs(Task task, Func<Task, bool> predicate) {
            if (task is ParallelTasks || task is SequentialTasks) {
                return Traverse.Create(task)
                    .Field("tasks")
                    .GetValue<IEnumerable<Task>>()
                    .Aggregate(null as Task, (found, t) => found == null ? TaskFindDfs(t, predicate) : found);
            }
            return predicate(task) ? task : null;
        }
    }
}