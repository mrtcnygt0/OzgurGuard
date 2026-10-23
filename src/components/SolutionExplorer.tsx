import React, { useState } from 'react';
import { 
  FolderTree, 
  FileCode, 
  Copy, 
  Check, 
  Download, 
  Terminal, 
  ShieldAlert, 
  Server, 
  Laptop
} from 'lucide-react';

interface FileEntry {
  path: string;
  category: 'Solution' | 'Service' | 'LockerUI' | 'Scripts';
  description: string;
}

const FILES: FileEntry[] = [
  { path: 'OzgurGuard.sln', category: 'Solution', description: 'Visual Studio 2022 / .NET 8 Çözüm Dosyası' },
  { path: 'install.ps1', category: 'Scripts', description: 'Windows Server Otomatik Kurulum ve Servis Kayıt Betiği' },
  { path: 'OzgurGuard.Service/Program.cs', category: 'Service', description: 'Windows Service Giriş Noktası & set-password CLI' },
  { path: 'OzgurGuard.Service/Services/SessionWatchdog.cs', category: 'Service', description: '300ms Heartbeat Bekçisi, Zaman Aşımı & NamedPipe Protokolü' },
  { path: 'OzgurGuard.Service/Services/SessionWatcherService.cs', category: 'Service', description: 'WTS Oturum Olaylarını Dinleyen BackgroundService' },
  { path: 'OzgurGuard.Service/Native/NativeMethods.cs', category: 'Service', description: 'WTSQueryUserToken & CreateProcessAsUser Win32 API' },
  { path: 'OzgurGuard.Service/Security/PasswordManager.cs', category: 'Service', description: 'PBKDF2 SHA-256 (100.000 iterasyon) Sabit Zamanlı Doğrulama' },
  { path: 'OzgurGuard.Service/Security/SecurityLogger.cs', category: 'Service', description: 'C:\\ProgramData\\OzgurGuard\\security.log Denetim Günlüğü' },
  { path: 'OzgurGuard.Service/appsettings.json', category: 'Service', description: 'Servis Yapılandırması ve Hash Parametreleri' },
  { path: 'OzgurGuard.LockerUI/MainWindow.xaml', category: 'LockerUI', description: 'WPF Kilit Arayüzü (Cam Efektli Koyu Kalkan Tasarımı)' },
  { path: 'OzgurGuard.LockerUI/MainWindow.xaml.cs', category: 'LockerUI', description: 'Klavye Kancası, TaskMgr Kilidi & Odak Tuzağı Yönetimi' },
  { path: 'OzgurGuard.LockerUI/Native/NativeMethods.cs', category: 'LockerUI', description: 'WH_KEYBOARD_LL Kancası & DACL Süreç Koruması' },
  { path: 'OzgurGuard.LockerUI/App.xaml.cs', category: 'LockerUI', description: 'Komut Satırı Argüman Ayrıştırma & Başlatma' },
];

export const SolutionExplorer: React.FC = () => {
  const [selectedFile, setSelectedFile] = useState<string>('install.ps1');
  const [fileContent, setFileContent] = useState<string>('');
  const [copied, setCopied] = useState(false);
  const [loading, setLoading] = useState(false);

  React.useEffect(() => {
    // Load file content dynamically or show placeholder
    loadFile(selectedFile);
  }, [selectedFile]);

  const loadFile = async (path: string) => {
    setLoading(true);
    try {
      // In web environment, we can fetch local source files
      const res = await fetch(`/${path}`);
      if (res.ok) {
        const text = await res.text();
        setFileContent(text);
      } else {
        setFileContent(`// ${path} kaynak dosyası diskte oluşturuldu.`);
      }
    } catch {
      setFileContent(`// ${path} kaynak dosyası diskte oluşturuldu.`);
    } finally {
      setLoading(false);
    }
  };

  const handleCopy = () => {
    navigator.clipboard.writeText(fileContent);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const handleDownload = () => {
    const blob = new Blob([fileContent], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = selectedFile.split('/').pop() || 'file.txt';
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  };

  return (
    <div className="space-y-6 text-slate-200">
      {/* Quick Setup Instructions Header */}
      <div className="bg-[#111827] border border-[#334155] rounded-xl p-5 shadow-lg">
        <div className="flex items-center gap-3 mb-3">
          <div className="w-9 h-9 rounded-lg bg-blue-600/20 border border-blue-500/30 flex items-center justify-center text-blue-400">
            <Terminal className="w-5 h-5" />
          </div>
          <div>
            <h3 className="font-semibold text-white text-base">Windows Server (VDS) Kurulum ve Çalıştırma Rehberi</h3>
            <p className="text-xs text-slate-400">
              Bu .NET 8.0 çözümünü Windows Server 2019 / 2022 / 2025 üzerine kurmak için aşağıdaki adımları izleyin:
            </p>
          </div>
        </div>

        <div className="bg-slate-950 p-4 rounded-lg border border-slate-800 text-xs font-mono space-y-2 text-slate-300 overflow-x-auto">
          <div className="text-slate-500"># 1. PowerShell&apos;i Yönetici Olarak (Run as Administrator) açın:</div>
          <div className="text-emerald-400">Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass</div>
          <div className="text-slate-500 mt-2"># 2. Kurulum betiğini çalıştırın (Otomatik derler, servisi kaydeder ve başlatır):</div>
          <div className="text-emerald-400">.\install.ps1</div>
          <div className="text-slate-500 mt-2"># 3. Şifreyi değiştirmek isterseniz:</div>
          <div className="text-emerald-400">
            &quot;C:\Program Files\OzgurGuard\Service\OzgurGuard.Service.exe&quot; set-password &quot;YeniGuvenliSifreniz2026!&quot;
          </div>
          <div className="text-emerald-400">Restart-Service OzgurGuard</div>
          <div className="text-slate-500 mt-2"># 4. Güvenlik günlüklerini canlı izlemek için:</div>
          <div className="text-emerald-400">Get-Content C:\ProgramData\OzgurGuard\security.log -Wait</div>
        </div>
      </div>

      {/* Code Inspector & File Tree */}
      <div className="bg-[#111827] border border-[#334155] rounded-xl shadow-lg overflow-hidden grid grid-cols-1 md:grid-cols-12 min-h-[540px]">
        {/* Left: Solution Tree */}
        <div className="md:col-span-4 bg-slate-950 border-r border-[#1E293B] p-4 flex flex-col justify-between">
          <div>
            <div className="flex items-center gap-2 text-xs font-bold text-slate-300 uppercase tracking-wider mb-3">
              <FolderTree className="w-4 h-4 text-blue-400" />
              <span>Çözüm Gezgini (Solution Files)</span>
            </div>

            <div className="space-y-1">
              {FILES.map((f) => (
                <button
                  key={f.path}
                  type="button"
                  onClick={() => setSelectedFile(f.path)}
                  className={`w-full text-left px-3 py-2 rounded-lg text-xs font-mono transition-colors flex items-center justify-between cursor-pointer ${
                    selectedFile === f.path
                      ? 'bg-blue-600/20 text-blue-300 border border-blue-500/40'
                      : 'text-slate-400 hover:text-slate-200 hover:bg-slate-900'
                  }`}
                >
                  <div className="flex items-center gap-2 truncate">
                    {f.category === 'Service' ? (
                      <Server className="w-3.5 h-3.5 text-blue-400 shrink-0" />
                    ) : f.category === 'LockerUI' ? (
                      <Laptop className="w-3.5 h-3.5 text-emerald-400 shrink-0" />
                    ) : (
                      <FileCode className="w-3.5 h-3.5 text-amber-400 shrink-0" />
                    )}
                    <span className="truncate">{f.path.split('/').pop()}</span>
                  </div>
                  <span className="text-[10px] text-slate-500 bg-slate-900 px-1.5 py-0.5 rounded ml-2 shrink-0">
                    {f.category}
                  </span>
                </button>
              ))}
            </div>
          </div>

          <div className="mt-6 pt-4 border-t border-slate-800 text-[11px] text-slate-400">
            <div className="flex items-center gap-1.5 text-slate-300 font-semibold mb-1">
              <ShieldAlert className="w-3.5 h-3.5 text-amber-400" />
              <span>Güvenlik Mimarisi</span>
            </div>
            <p className="leading-relaxed">
              Bu kodlar Windows Server üzerinde interaktif olmayan IIS, SQL veya HTTP servislerine dokunmadan yalnızca RDP/Konsol oturumlarını kilitler.
            </p>
          </div>
        </div>

        {/* Right: Code Viewer */}
        <div className="md:col-span-8 flex flex-col bg-[#0B0F19]">
          {/* File Header & Actions */}
          <div className="h-12 border-b border-[#1E293B] bg-slate-900/80 px-4 flex items-center justify-between">
            <div className="flex items-center gap-2 text-xs font-mono text-slate-300">
              <FileCode className="w-4 h-4 text-blue-400" />
              <span>{selectedFile}</span>
            </div>
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={handleCopy}
                className="px-3 py-1 bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs rounded border border-slate-700 transition-colors flex items-center gap-1.5 cursor-pointer"
              >
                {copied ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
                <span>{copied ? 'Kopyalandı' : 'Kodu Kopyala'}</span>
              </button>
              <button
                type="button"
                onClick={handleDownload}
                className="px-3 py-1 bg-blue-600 hover:bg-blue-500 text-white text-xs rounded font-medium transition-colors flex items-center gap-1.5 cursor-pointer shadow-sm"
              >
                <Download className="w-3.5 h-3.5" />
                <span>İndir</span>
              </button>
            </div>
          </div>

          {/* Code Text Content */}
          <div className="flex-1 p-4 overflow-auto font-mono text-xs text-slate-300 bg-[#070A12] leading-relaxed">
            {loading ? (
              <div className="text-slate-500">Yükleniyor...</div>
            ) : (
              <pre className="whitespace-pre-wrap">{fileContent}</pre>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
