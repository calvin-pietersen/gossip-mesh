using System;
using System.Collections;
using System.Collections.Generic;

namespace GossipMesh.Core
{
	// see T. E. Hull and A. R. Dobell (1962). ‘Random number generators.’ Soc. Indust. Appl. Math. Rev. 4, 230–254.
	public class RandomVisit<T> : IEnumerable<T>
	{
		// used when selecting a prime to limit search space from the prime
		private const int MAX_COUNT = 100000;

		private readonly IReadOnlyList<T> _backingCollection;

		private readonly int _maxRange;
		private readonly int _prime;
		private readonly int _offset;

		private int _index;
		private int _runningValue;

		public RandomVisit(IReadOnlyList<T> collection)
		{
			_backingCollection = collection;

			var range = collection.Count;

			if (range < 2)
				throw new ArgumentOutOfRangeException("Your range needs to be greater than 1 " + range);

			int min = range / 2;
			_maxRange = range;
			_prime = SelectCoPrimeResev(min, range);
			_offset =  new Random().Next(range);
			_index = 0;
			_runningValue = _offset;
		}

		public bool HasNext()
		{
			return _index < _maxRange;
		}

		private int Next()
		{
			_runningValue += _prime;
			if (_runningValue >= _maxRange)
				_runningValue -= _maxRange;
			_index++;

			return _runningValue;
		}

		private static int SelectCoPrimeResev(int min, int target)
		{
			int count = 0;
			int selected = 0;
			var rand = new Random();
			for (int val = min; val < target; ++val)
			{
				if (Coprime(val, target))
				{
					count += 1;
					if ((count == 1) || (rand.Next(count) < 1))
					{
						selected = val;
					}
				};
				if (count == MAX_COUNT)
					return val;
			}
			return selected;
		}

		private static bool Coprime(int u, int v)
		{
			return gcd(u, v) == 1;
		}

		private static int gcd(int u, int v)
		{
			int shift;
			if (u == 0)
				return v;
			if (v == 0)
				return u;
			for (shift = 0; ((u | v) & 1) == 0; ++shift)
			{
				u >>= 1;
				v >>= 1;
			}

			while ((u & 1) == 0)
				u >>= 1;

			do
			{
				while ((v & 1) == 0)
					v >>= 1;
				if (u > v)
				{
					int t = v;
					v = u;
					u = t;
				}
				v = v - u;
			} while (v != 0);
			return u << shift;
		}

		public IEnumerator<T> GetEnumerator()
		{
			while (HasNext())
			{
				yield return _backingCollection[Next()];
			}
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}
}
