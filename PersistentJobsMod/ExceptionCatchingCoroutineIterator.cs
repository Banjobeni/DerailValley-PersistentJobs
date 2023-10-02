using System;
using System.Collections;
using System.Collections.Generic;

namespace PersistentJobsMod {
    public class ExceptionCatchingCoroutineIterator : IEnumerator {
        private readonly IEnumerator<(string NextStageName, object Result)> _nestedEnumerator;
        private readonly string _iteratorName;
        private string _nextStageName;

        public ExceptionCatchingCoroutineIterator(IEnumerator<(string NextStageName, object Result)> nestedEnumerator, string iteratorName) {
            _nestedEnumerator = nestedEnumerator;
            _iteratorName = iteratorName;
            _nextStageName = "initial";
        }

        public bool MoveNext() {
            try {
                var moveNextResult = _nestedEnumerator.MoveNext();
                if (moveNextResult) {
                    _nextStageName = _nestedEnumerator.Current.NextStageName;
                }
                return moveNextResult;
            } catch (Exception e) {
                Main.HandleUnhandledException(e, $"iterator {_iteratorName} after yield at \"{_nextStageName}\"");
                return false;
            }
        }

        public void Reset() {
            _nestedEnumerator.Reset();
        }

        public object Current => _nestedEnumerator.Current.Result;
    }
}