Shader "Cloud/CirrusDome"
{
    //IDEA 
    //Simple Perlin Noise
    //FBM to add details 
    //Stretch noise in wind direction to create cirrus shape
    Properties
    {
        _BaseColor   ("Color",    Color)           = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline"  = "UniversalPipeline"
        }

        Pass
        {
            Name "Cirrus"

            // Tag to be used in the renderpass
            Tags { "LightMode" = "CirrusDome" }

            Cull   Front            
            ZWrite Off
            ZTest  LEqual
            Blend  SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float4 _BaseColor;

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 viewDirWS  : TEXCOORD0;
            };


            //PERLINE NOISE + HASH FROM https://www.reddit.com/r/unity/comments/19b8yz4/made_my_own_perlin_noise_function_for_shaders/
            float hash(float2 i) {

                uint n = i.x + i.y * 100;

                // Integer hash from Hugo Elias

                n = (n << 13U) ^ n;

                n = n * (n * n * 15731U + 0x789221U) + 0x1376312589U;

                return float(n & uint(0x7fffffffU)) / float(0x7fffffff);
            }

            float ease(float a, float b, float t) {
                // InOutSine function from Kryzarel

                return a + ((float)(cos(t * 3.14159265359f) - 1) / -2) * (b - a);
            }

            float perlinnoise(float x, float y) {

                float2 AA = float2(floor(x), floor(y));

                float2 AB = float2(AA.x + 1, AA.y);

                float2 BA = float2(AA.x, AA.y + 1);

                float2 BB = float2(AA.x + 1, AA.y + 1);

                float A = ease(hash(AA), hash(AB), x - AA.x);

                float B = ease(hash(BA), hash(BB), x - BA.x);

                return ease(A, B, y - AA.y);
            }
            // 5-octave fractal Brownian motion, idea is the same as worley etc. noise
            float fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
    
                for (int i = 0; i < 5; i++)
                {
                    v += a * perlinnoise(p.x,p.y);
                    
                    p  = p * 2 + float2(15.0, 10.0); //Sample different parts of noise
                    a *= 0.5; //0.5*0.25*0.125..
                }
                return v-0.65;//Tuned for right coverage
            }

            //Camera Parameters
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 worldPos = TransformObjectToWorld(IN.positionOS);
                OUT.positionCS  = TransformWorldToHClip(worldPos);
                OUT.viewDirWS   = worldPos - _WorldSpaceCameraPos.xyz;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 v = normalize(IN.viewDirWS);

                // Fade into Horizon
                float horizonAlpha = smoothstep(0.0, 0.28, v.y);
                if (horizonAlpha <= 0.0) return float4(0,0,0,0);

                // Simullate a plane directly above with infinite size, which point am i looking at?
                float invY = 1.0 / max(v.y, 0.001);
                float2 planar = float2(v.x, v.z) * invY ;

                float2 windDir = float2(1, 1);//Aligns with clouds movement
                float2 perpDir = float2(-windDir.y, windDir.x); 

                // Stretch noise to create shape
                float along = dot(planar, windDir);
                float across = dot(planar, perpDir);
                float2 uv = float2(along / 5,across);

                // fbm twice to get much noise and details
                float noise1  = fbm(uv);
                float noise2 = fbm(uv * 4 + 10.0);
                float noise = saturate(0.5 * noise1 + 0.3 * noise2 );

                float alpha = noise * horizonAlpha;
                return float4(_BaseColor.rgb , alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
