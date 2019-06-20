using Unity.Mathematics;
using static Unity.Mathematics.math;

public static class NoiseExt {
	public struct NoiseSample1 {
		public float val;
		public float gradient;

		//// Warning: float values are assumend to be constants over noise input space, ie. dont multiply,add,sub a NoiseSample1 with pos.x or anything resulting from pos
		
		// (f(x) + c)' = f'(x)
		public static NoiseSample1 operator+ (NoiseSample1 l, float r) {
			l.val += r;
			return l;
		}
		// (c + f(x))' = f'(x)
		public static NoiseSample1 operator+ (float l, NoiseSample1 r) {
			r.val += l;
			return r;
		}
		// (f(x) + g(x))' = f'(x) + g'(x)
		public static NoiseSample1 operator+ (NoiseSample1 l, NoiseSample1 r) {
			l.val += r.val;
			l.gradient += r.gradient;
			return l;
		}
		
		// (f(x) - c)' = f'(x)
		public static NoiseSample1 operator- (NoiseSample1 l, float r) {
			l.val -= r;
			return l;
		}
		// (c - f(x))' = -f'(x)
		public static NoiseSample1 operator- (float l, NoiseSample1 r) {
			r.val = l -r.val;
			r.gradient = -r.gradient;
			return r;
		}
		// (f(x) - g(x))' = f'(x) - g'(x)
		public static NoiseSample1 operator- (NoiseSample1 l, NoiseSample1 r) {
			l.val -= r.val;
			l.gradient -= r.gradient;
			return l;
		}
		
		// (f(x) * c)' = f'(x) * c
		public static NoiseSample1 operator* (NoiseSample1 l, float r) {
			l.val *= r;
			l.gradient *= r;
			return l;
		}
		// (c * f(x))' = c * f'(x)
		public static NoiseSample1 operator* (float l, NoiseSample1 r) {
			r.val *= l;
			r.gradient *= l;
			return r;
		}
		// (f(x) * g(x))' = f(x) * g'(x) + f'(x) * g(x)
		public static NoiseSample1 operator* (NoiseSample1 l, NoiseSample1 r) {
			l.val = l.val * r.val;
			l.gradient = l.val * r.gradient + l.gradient * r.val;
			return l;
		}
	}
	public struct NoiseSample2 {
		public float val;
		public float2 gradient;
		
		// (f(x) + c)' = f'(x)
		public static NoiseSample2 operator+ (NoiseSample2 l, float r) {
			l.val += r;
			return l;
		}
		// (c + f(x))' = f'(x)
		public static NoiseSample2 operator+ (float l, NoiseSample2 r) {
			r.val += l;
			return r;
		}
		// (f(x) + g(x))' = f'(x) + g'(x)
		public static NoiseSample2 operator+ (NoiseSample2 l, NoiseSample2 r) {
			l.val += r.val;
			l.gradient += r.gradient;
			return l;
		}
		
		// (f(x) - c)' = f'(x)
		public static NoiseSample2 operator- (NoiseSample2 l, float r) {
			l.val -= r;
			return l;
		}
		// (c - f(x))' = -f'(x)
		public static NoiseSample2 operator- (float l, NoiseSample2 r) {
			r.val = l -r.val;
			r.gradient = -r.gradient;
			return r;
		}
		// (f(x) - g(x))' = f'(x) - g'(x)
		public static NoiseSample2 operator- (NoiseSample2 l, NoiseSample2 r) {
			l.val -= r.val;
			l.gradient -= r.gradient;
			return l;
		}
		
		// (f(x) * c)' = f'(x) * c
		public static NoiseSample2 operator* (NoiseSample2 l, float r) {
			l.val *= r;
			l.gradient *= r;
			return l;
		}
		// (c * f(x))' = c * f'(x)
		public static NoiseSample2 operator* (float l, NoiseSample2 r) {
			r.val *= l;
			r.gradient *= l;
			return r;
		}
		// (f(x) * g(x))' = f(x) * g'(x) + f'(x) * g(x)
		public static NoiseSample2 operator* (NoiseSample2 l, NoiseSample2 r) {
			l.val = l.val * r.val;
			l.gradient = l.val * r.gradient + l.gradient * r.val;
			return l;
		}
	}
	public struct NoiseSample3 {
		public float val;
		public float3 gradient;
		
		// (f(x) + c)' = f'(x)
		public static NoiseSample3 operator+ (NoiseSample3 l, float r) {
			l.val += r;
			return l;
		}
		// (c + f(x))' = f'(x)
		public static NoiseSample3 operator+ (float l, NoiseSample3 r) {
			r.val += l;
			return r;
		}
		// (f(x) + g(x))' = f'(x) + g'(x)
		public static NoiseSample3 operator+ (NoiseSample3 l, NoiseSample3 r) {
			l.val += r.val;
			l.gradient += r.gradient;
			return l;
		}
		
		// (f(x) - c)' = f'(x)
		public static NoiseSample3 operator- (NoiseSample3 l, float r) {
			l.val -= r;
			return l;
		}
		// (c - f(x))' = -f'(x)
		public static NoiseSample3 operator- (float l, NoiseSample3 r) {
			r.val = l -r.val;
			r.gradient = -r.gradient;
			return r;
		}
		// (f(x) - g(x))' = f'(x) - g'(x)
		public static NoiseSample3 operator- (NoiseSample3 l, NoiseSample3 r) {
			l.val -= r.val;
			l.gradient -= r.gradient;
			return l;
		}
		
		// (f(x) * c)' = f'(x) * c
		public static NoiseSample3 operator* (NoiseSample3 l, float r) {
			l.val *= r;
			l.gradient *= r;
			return l;
		}
		// (c * f(x))' = c * f'(x)
		public static NoiseSample3 operator* (float l, NoiseSample3 r) {
			r.val *= l;
			r.gradient *= l;
			return r;
		}
		// (f(x) * g(x))' = f(x) * g'(x) + f'(x) * g(x)
		public static NoiseSample3 operator* (NoiseSample3 l, NoiseSample3 r) {
			l.val = l.val * r.val;
			l.gradient = l.val * r.gradient + l.gradient * r.val;
			return l;
		}
	}
	
	public static NoiseSample1 snoise (float v) {
		var sampl = new NoiseSample1();
	#if true
		float3 deriv3;
		sampl.val = noise.snoise(float3(v, 0f, 0f), out deriv3);
		sampl.gradient = deriv3.x;
	#else
		float3 val = noise.srdnoise(float2(v, 0));
		sampl.val = val.x;
		sampl.gradient = val.y;
	#endif
		return sampl;
	}
	public static NoiseSample2 snoise (float2 v) {
		var sampl = new NoiseSample2();
	#if true
		float3 deriv3;
		sampl.val = noise.snoise(float3(v, 0f), out deriv3);
		sampl.gradient = deriv3.xy;
	#else
		float3 val = noise.srdnoise(v);
		sampl.val = val.x;
		sampl.gradient = val.yz;
	#endif
		return sampl;
	}
	public static NoiseSample3 snoise (float3 v) {
		var sampl = new NoiseSample3();
		sampl.val = noise.snoise(v, out sampl.gradient);
		return sampl;
	}
	
	public static NoiseSample1 fsnoise (float pos, int octaves, float freq, bool dampen=true) {
		float dampened = dampen ? freq : 1f;
		var total = snoise(pos / freq);
		float amplitude = 1.0f;
		float range = 1.0f;
		for (int i=1; i<octaves; ++i) {
			freq /= 2;
			amplitude /= 2;
			range += amplitude;
			total += snoise(pos / freq) * amplitude;
		}
		return total * (dampened / range);
	}
	public static NoiseSample2 fsnoise (float2 pos, int octaves, float freq, bool dampen=true) {
		float dampened = dampen ? freq : 1f;
		var total = snoise(pos / freq);
		float amplitude = 1.0f;
		float range = 1.0f;
		for (int i=1; i<octaves; ++i) {
			freq /= 2;
			amplitude /= 2;
			range += amplitude;
			total += snoise(pos / freq) * amplitude;
		}
		return total * (dampened / range);
	}
	public static NoiseSample3 fsnoise (float3 pos, int octaves, float freq, bool dampen=true) {
		float dampened = dampen ? freq : 1f;
		var total = snoise(pos / freq);
		float amplitude = 1.0f;
		float range = 1.0f;
		for (int i=1; i<octaves; ++i) {
			freq /= 2;
			amplitude /= 2;
			range += amplitude;
			total += snoise(pos / freq) * amplitude;
		}
		return total * (dampened / range);
	}
}
