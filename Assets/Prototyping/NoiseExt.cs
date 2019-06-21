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
		
		// TODO: Div not tested well
		// (f(x) / c)' = f'(x) / c
		public static NoiseSample1 operator/ (NoiseSample1 l, float r) {
			l.val /= r;
			l.gradient /= r;
			return l;
		}
		// (c / f(x))' = (-c * f'(x)) / (f(x)^2)
		public static NoiseSample1 operator/ (float l, NoiseSample1 r) {
			r.val = l / r.val;
			r.gradient = (-l * r.gradient) / (r.val * r.val);
			return r;
		}
		// (f(x) / g(x))' = (f'(x) / g(x) - g'(x) / f(x)) / (g(x)^2)
		public static NoiseSample1 operator/ (NoiseSample1 l, NoiseSample1 r) {
			l.val = l.val / r.val;
			l.gradient = (l.gradient * r.val + r.gradient * l.val) / (r.val * r.val);
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

		// TODO: Div not tested well
		// (f(x) / c)' = f'(x) / c
		public static NoiseSample2 operator/ (NoiseSample2 l, float r) {
			l.val /= r;
			l.gradient /= r;
			return l;
		}
		// (c / f(x))' = (-c * f'(x)) / (f(x)^2)
		public static NoiseSample2 operator/ (float l, NoiseSample2 r) {
			r.val = l / r.val;
			r.gradient = (-l * r.gradient) / (r.val * r.val);
			return r;
		}
		// (f(x) / g(x))' = (f'(x) / g(x) - g'(x) / f(x)) / (g(x)^2)
		public static NoiseSample2 operator/ (NoiseSample2 l, NoiseSample2 r) {
			l.val = l.val / r.val;
			l.gradient = (l.gradient * r.val + r.gradient * l.val) / (r.val * r.val);
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
			l.gradient = l.val * r.gradient + l.gradient * r.val;
			l.val = l.val * r.val;
			return l;
		}
		
		// TODO: Div not tested well
		// (f(x) / c)' = f'(x) / c
		public static NoiseSample3 operator/ (NoiseSample3 l, float r) {
			l.val /= r;
			l.gradient /= r;
			return l;
		}
		// (c / f(x))' = (-c * f'(x)) / (f(x)^2)
		public static NoiseSample3 operator/ (float l, NoiseSample3 r) {
			r.val = l / r.val;
			r.gradient = (-l * r.gradient) / (r.val * r.val);
			return r;
		}
		// (f(x) / g(x))' = (f'(x) / g(x) - g'(x) / f(x)) / (g(x)^2)
		public static NoiseSample3 operator/ (NoiseSample3 l, NoiseSample3 r) {
			l.val = l.val / r.val;
			l.gradient = (l.gradient * r.val + r.gradient * l.val) / (r.val * r.val);
			return l;
		}
	}
	
	public static NoiseSample3 sqrt (NoiseSample3 x) {
		x.val = math.sqrt(x.val);
		x.gradient = 0.5f / x.val * x.gradient;
		return x;
	}

	// WARNING: you cannot scale or otherwise modify the position input of the noise functions, since this will not be respected in the gradient
	//  f(    x) =>     f'(x)
	//  f(2 * x) => 2 * f'(x)
	// TODO: implement NoiseSample1as input to snoise(float) ?
	//   

	public static NoiseSample1 snoise (float pos, float invFreq) {
		var sampl = new NoiseSample1();
	#if true
		float3 deriv3;
		sampl.val = noise.snoise(float3(pos * invFreq, 0f, 0f), out deriv3);
		sampl.gradient = deriv3.x * invFreq;
	#else
		float3 val = noise.srdnoise(float2(v * invFreq, 0));
		sampl.val = val.x;
		sampl.gradient = val.y * invFreq;
	#endif
		return sampl;
	}
	public static NoiseSample2 snoise (float2 pos, float2 invFreq) {
		var sampl = new NoiseSample2();
	#if true
		float3 deriv3;
		sampl.val = noise.snoise(float3(pos * invFreq, 0f), out deriv3);
		sampl.gradient = deriv3.xy * invFreq;
	#else
		float3 val = noise.srdnoise(v * invFreq);
		sampl.val = val.x;
		sampl.gradient = val.yz * invFreq;
	#endif
		return sampl;
	}
	public static NoiseSample3 snoise (float3 pos, float3 invFreq) {
		var sampl = new NoiseSample3();
		sampl.val = noise.snoise(pos * invFreq, out sampl.gradient);
		sampl.gradient *= invFreq;
		return sampl;
	}
	
	public static NoiseSample1 fsnoise (float pos, float freq, int octaves, bool dampen=false) {
		float dampened = dampen ? freq : 1f;
		float invFreq = 1f / freq;

		var total = snoise(pos, invFreq);
		float amplitude = 1.0f;
		float range = 1.0f;
		for (int i=1; i<octaves; ++i) {
			invFreq *= 2;
			amplitude *= 0.5f;
			range += amplitude;
			total += snoise(pos, invFreq) * amplitude;
		}
		return total * (dampened / range);
	}
	public static NoiseSample2 fsnoise (float2 pos, float freq, int octaves, bool dampen=false) {
		float dampened = dampen ? freq : 1f;
		float invFreq = 1f / freq;

		var total = snoise(pos, invFreq);
		float amplitude = 1.0f;
		float range = 1.0f;
		for (int i=1; i<octaves; ++i) {
			invFreq *= 2;
			amplitude *= 0.5f;
			range += amplitude;
			total += snoise(pos, invFreq) * amplitude;
		}
		return total * (dampened / range);
	}
	public static NoiseSample3 fsnoise (float3 pos, float freq, int octaves, bool dampen=false) {
		float dampened = dampen ? freq : 1f;
		float invFreq = 1f / freq;

		var total = snoise(pos, invFreq);
		float amplitude = 1.0f;
		float range = 1.0f;
		for (int i=1; i<octaves; ++i) {
			invFreq *= 2;
			amplitude *= 0.5f;
			range += amplitude;
			total += snoise(pos, invFreq) * amplitude;
		}
		return total * (dampened / range);
	}
}
