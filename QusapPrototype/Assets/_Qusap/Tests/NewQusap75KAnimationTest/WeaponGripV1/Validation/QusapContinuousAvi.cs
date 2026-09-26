using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Streaming MJPEG AVI from actual rendered PlayerLoop frames. No animation sampling.
public sealed class QusapContinuousAvi : IDisposable
{
    private readonly BinaryWriter writer;
    private readonly List<(uint offset,uint size)> index=new List<(uint,uint)>();
    private long mainFrames,streamFrames,moviSize,moviStart;
    public int Frames=>index.Count;
    public QusapContinuousAvi(string path,int width,int height,int fps)
    {
        writer=new BinaryWriter(File.Create(path));
        Four("RIFF");writer.Write(0u);Four("AVI ");
        long hdrl=List("hdrl");Four("avih");writer.Write(56u);
        writer.Write(1000000u/(uint)fps);writer.Write((uint)(width*height*3*fps));writer.Write(0u);writer.Write(0x10u);
        mainFrames=writer.BaseStream.Position;writer.Write(0u);writer.Write(0u);writer.Write(1u);writer.Write((uint)(width*height*3));writer.Write((uint)width);writer.Write((uint)height);
        for(int i=0;i<4;i++)writer.Write(0u);
        long strl=List("strl");Four("strh");writer.Write(56u);Four("vids");Four("MJPG");writer.Write(0u);writer.Write((ushort)0);writer.Write((ushort)0);writer.Write(0u);
        writer.Write(1u);writer.Write((uint)fps);writer.Write(0u);streamFrames=writer.BaseStream.Position;writer.Write(0u);writer.Write((uint)(width*height*3));writer.Write(uint.MaxValue);writer.Write(0u);
        writer.Write((short)0);writer.Write((short)0);writer.Write((short)width);writer.Write((short)height);
        Four("strf");writer.Write(40u);writer.Write(40u);writer.Write(width);writer.Write(height);writer.Write((ushort)1);writer.Write((ushort)24);Four("MJPG");writer.Write(width*height*3);
        for(int i=0;i<4;i++)writer.Write(0u);
        EndList(strl);EndList(hdrl);
        moviSize=List("movi");moviStart=writer.BaseStream.Position-4;
    }
    public void Add(byte[] jpeg)
    {
        index.Add(((uint)(writer.BaseStream.Position-moviStart),(uint)jpeg.Length));
        Four("00dc");writer.Write((uint)jpeg.Length);writer.Write(jpeg);if((jpeg.Length&1)!=0)writer.Write((byte)0);
    }
    private void Four(string s)=>writer.Write(Encoding.ASCII.GetBytes(s));
    private long List(string type){Four("LIST");long p=writer.BaseStream.Position;writer.Write(0u);Four(type);return p;}
    private void Patch(long p,uint value){long end=writer.BaseStream.Position;writer.BaseStream.Position=p;writer.Write(value);writer.BaseStream.Position=end;}
    private void EndList(long p)=>Patch(p,(uint)(writer.BaseStream.Position-p-4));
    public void Dispose()
    {
        EndList(moviSize);Four("idx1");writer.Write((uint)(index.Count*16));
        foreach(var entry in index){Four("00dc");writer.Write(0x10u);writer.Write(entry.offset);writer.Write(entry.size);}
        Patch(mainFrames,(uint)index.Count);Patch(streamFrames,(uint)index.Count);Patch(4,(uint)(writer.BaseStream.Length-8));writer.Dispose();
    }
}
