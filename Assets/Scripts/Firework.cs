using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using Random = UnityEngine.Random;

public class Firework : MonoBehaviour
{
    public struct Particle
    {
        public Vector3 position;
        public Vector3 velocity;
        public float duration;
        public Vector4 color;
        public int isBomb;
    }

    [SerializeField] private ComputeShader computeShader;
    [SerializeField] private Mesh mesh;
    [SerializeField] private Material material;

    ComputeBuffer particleBuffer;
    ComputeBuffer pooledParticleBuffer;
    ComputeBuffer particleCountBuffer;

    Particle[] particles;
    Color[] colors; // パーティクルの色を格納する配列
    uint[] particleCount; //0番目にパーティクルの数が入る

    int kernelIndexInitialize;
    int kernelIndexUpdate;
    int kernelIndexFire;
    int kernelIndexBomb;

    const int THREAD_NUM = 8;
    const int MAX_COUNT = 100000;
    const int EMIT_COUNT = MAX_BOMB * BOMB_DIVIDE;
    const int ALIGNMENT_MAX_COUNT = MAX_COUNT / THREAD_NUM * THREAD_NUM;
    const int ALIGNMENT_EMIT_COUNT = EMIT_COUNT / THREAD_NUM * THREAD_NUM;

    const int MIN_BOMB = 20; // 爆発の最小数
    const int MAX_BOMB = 50; // 爆発の最大数

    const int BOMB_DIVIDE = 20;

    private int counter = 0;

    private void Start()
    {
        // Compute Shader の処理のindexを取得
        kernelIndexInitialize = computeShader.FindKernel("Initialize");
        kernelIndexUpdate = computeShader.FindKernel("Update");
        kernelIndexFire = computeShader.FindKernel("Fire");
        kernelIndexBomb = computeShader.FindKernel("Bomb");

        // 描画に必要な情報のConpute Bufferを用意
        particleBuffer = new ComputeBuffer(ALIGNMENT_MAX_COUNT, Marshal.SizeOf(typeof(Particle)));
        particles = new Particle[ALIGNMENT_MAX_COUNT];
        particleBuffer.SetData(particles);

        // プールされたパーティクルID保存用Compute Bufferを用意
        pooledParticleBuffer =
            new ComputeBuffer(ALIGNMENT_MAX_COUNT, Marshal.SizeOf(typeof(uint)), ComputeBufferType.Append);
        pooledParticleBuffer.SetCounterValue(0);

        particleCountBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(uint)), ComputeBufferType.IndirectArguments);
        particleCount = new uint[] { 0 };
        particleCountBuffer.SetData(particleCount);

        // 初期化
        computeShader.SetBuffer(kernelIndexInitialize, "_DeadParticleBuffer", pooledParticleBuffer);
        computeShader.SetBuffer(kernelIndexUpdate, "_ParticleBuffer", particleBuffer);
        computeShader.SetBuffer(kernelIndexUpdate, "_PooledParticleBuffer", pooledParticleBuffer);
        computeShader.SetBuffer(kernelIndexUpdate, "_DeadParticleBuffer", pooledParticleBuffer);
        computeShader.SetBuffer(kernelIndexFire, "_PooledParticleBuffer", pooledParticleBuffer);
        computeShader.SetBuffer(kernelIndexFire, "_ParticleBuffer", particleBuffer);
        computeShader.SetBuffer(kernelIndexBomb, "_PooledParticleBuffer", pooledParticleBuffer);
        computeShader.SetBuffer(kernelIndexBomb, "_ParticleBuffer", particleBuffer);

        computeShader.SetInt("_MinBomb", MIN_BOMB);
        computeShader.SetInt("_MaxBomb", MAX_BOMB);

        // 描画用Shaderに対して情報を渡す
        material.SetBuffer("_ParticleBuffer", particleBuffer);

        // Initialize カーネルの実行
        computeShader.Dispatch(kernelIndexInitialize, ALIGNMENT_MAX_COUNT / THREAD_NUM, 1, 1);
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Vector3 mousePosition = Input.mousePosition + Vector3.forward * 10;
            Vector3 screen = Camera.main.ScreenToWorldPoint(mousePosition);
            screen.y = -4.5f;

            Fire(screen);
        }

        computeShader.SetFloat("_DeltaTime", Time.deltaTime);
        computeShader.SetFloat("_Time", Time.time);
        computeShader.Dispatch(kernelIndexUpdate, particleBuffer.count / THREAD_NUM, 1, 1);

        Graphics.DrawMeshInstancedProcedural(mesh, 0, material, new Bounds(Vector3.zero, Vector3.one * 100),
            ALIGNMENT_MAX_COUNT);
    }

    /// <summary>
    /// 発射
    /// </summary>
    void Fire(Vector3 position)
    {
        ComputeBuffer.CopyCount(pooledParticleBuffer, particleCountBuffer, 0);
        //バッファからデータを取得
        particleCountBuffer.GetData(particleCount);

        uint count = particleCount[0];
        if (count < ALIGNMENT_EMIT_COUNT)
        {
            // パーティクルの数が少ない場合は何もしない
            return;
        }

        //情報を設定
        Vector3 velocity = new Vector3(0, Random.Range(4f, 6f), 0);
        float duration = Random.Range(1f, 1.5f);
        computeShader.SetVector("_FirePosition", position);
        computeShader.SetVector("_FireVelocity", velocity);
        computeShader.SetFloat("_FireDuration", duration);

        computeShader.Dispatch(kernelIndexFire, 1, 1, 1);
        StartCoroutine(WaitBomb(duration, position, velocity));
    }

    private IEnumerator WaitBomb(float waitTime, Vector3 startPos, Vector3 fireVelocity)
    {
        yield return new WaitForSeconds(waitTime);

        Vector3 currentPosition = startPos + fireVelocity * waitTime;
        int bombCount = Random.Range(MIN_BOMB, MAX_BOMB);
        float speed = Random.Range(2.5f, 4f);
        
        float r = Random.Range(0f, 1f);
        float g = Random.Range(0f, 1f);
        float b = Mathf.Min(Random.Range(0f, 1f), 1 - r - g);
        Color[] colorArray = new []
        {
            Color.red, 
            Color.blue,
            Color.yellow,
            Color.green,
            Color.white,
        };
        Color color = colorArray[counter++ % colorArray.Length];

        float startRotate = Random.Range(0f, 2f * Mathf.PI);
        for (int i = 0; i < bombCount; i++)
        {
            float radian = 2f * Mathf.PI * i / bombCount;
            float rotate = radian + startRotate + Random.Range(0f, 0.3f / bombCount);       //少しだけずれを作る
            Vector3 baseVelocity = new Vector3(Mathf.Cos(rotate), Mathf.Sin(rotate), 0).normalized * speed;
            for (int j = 0; j < BOMB_DIVIDE; j++)
            {
                Vector3 bombVelocity = baseVelocity * (float)(j + 1) / BOMB_DIVIDE;
                Bomb(currentPosition, bombVelocity, color);
            }
        }
    }

    private void Bomb(Vector3 position, Vector3 bombVelocity, Vector4 color)
    {
        //情報を設定
        computeShader.SetVector("_FirePosition", position);
        computeShader.SetVector("_FireVelocity", bombVelocity);
        computeShader.SetFloat("_FireDuration", Random.Range(1f, 2f));

        computeShader.SetVector("_BombColor", color);

        computeShader.Dispatch(kernelIndexBomb, 1, 1, 1);
    }

    void OnGUI()
    {
        ComputeBuffer.CopyCount(pooledParticleBuffer, particleCountBuffer, 0);
        particleCountBuffer.GetData(particleCount);
        GUILayout.Label("Pooled(Dead) Particles : " + particleCount[0]);
    }

    private void OnDestroy()
    {
        particleBuffer.Release();
        pooledParticleBuffer.Release();
        particleCountBuffer.Release();
    }
}