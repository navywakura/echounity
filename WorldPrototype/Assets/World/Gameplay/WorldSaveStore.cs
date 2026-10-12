using System;
using System.IO;
using System.Text;

namespace EchoWorld
{
    // WorldData may flush streamed columns at any time. Those writes remain in a
    // working directory. Only a complete terrain+session snapshot advances HEAD.
    public sealed class WorldSaveStore
    {
        public readonly string Root,Working;
        string previous;
        public WorldSaveStore(string root)
        {
            Root=root;Directory.CreateDirectory(root);
            string head=Path.Combine(root,"CURRENT");
            if(File.Exists(head))
            {
                previous=File.ReadAllText(head).Trim();
                if(previous.Length!=32||!Guid.TryParseExact(previous,"N",out _))throw new InvalidDataException("Invalid save checkpoint pointer");
                if(!Directory.Exists(Path.Combine(root,"checkpoints",previous)))throw new InvalidDataException("Missing committed checkpoint");
            }
            Working=Path.Combine(root,"working-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Working);
            if(previous!=null)Copy(Path.Combine(root,"checkpoints",previous),Working);
        }
        static void Copy(string source,string target)
        {
            Directory.CreateDirectory(target);
            foreach(string file in Directory.EnumerateFiles(source,"*",SearchOption.TopDirectoryOnly))
            {
                string name=Path.GetFileName(file);
                if(name=="session-v2.json"||(name.StartsWith("column_")&&name.EndsWith(".ewd")))
                    File.Copy(file,Path.Combine(target,name),true);
            }
        }
        public void Commit()
        {
            if(!File.Exists(Path.Combine(Working,"session-v2.json")))throw new InvalidDataException("Incomplete save: missing session");
            string id=Guid.NewGuid().ToString("N"),snapshots=Path.Combine(Root,"checkpoints"),destination=Path.Combine(snapshots,id);
            Copy(Working,destination);
            // Flush every file before publishing the small pointer. Readers never
            // combine a previous inventory with the next generation of terrain.
            foreach(string file in Directory.EnumerateFiles(destination))
                using(var stream=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None))stream.Flush(true);
            string head=Path.Combine(Root,"CURRENT"),temp=head+".tmp";
            byte[] bytes=Encoding.ASCII.GetBytes(id+"\n");
            using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None))
            {stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
            if(File.Exists(head))File.Replace(temp,head,head+".bak");else File.Move(temp,head);
            string keep=previous;previous=id;
            // Housekeeping is best effort; a valid commit remains valid if cleanup fails.
            try
            {
                foreach(string directory in Directory.EnumerateDirectories(snapshots))
                    if(Path.GetFileName(directory)!=id&&Path.GetFileName(directory)!=keep)Directory.Delete(directory,true);
                foreach(string directory in Directory.EnumerateDirectories(Root,"working-*"))
                    if(directory!=Working)Directory.Delete(directory,true);
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}
