import React, { useState } from 'react';
import { SecurityLogEntry } from '../types';
import { 
  FileText, 
  AlertCircle, 
  AlertTriangle, 
  Info, 
  Trash2, 
  Download, 
  Search,
  Filter
} from 'lucide-react';

interface EventLogViewerProps {
  logs: SecurityLogEntry[];
  onClearLogs: () => void;
}

export const EventLogViewer: React.FC<EventLogViewerProps> = ({ logs, onClearLogs }) => {
  const [filterLevel, setFilterLevel] = useState<'All' | 'INFO' | 'WARN' | 'ERROR'>('All');
  const [searchTerm, setSearchTerm] = useState('');

  const filteredLogs = logs.filter((item) => {
    if (filterLevel !== 'All' && item.level !== filterLevel) {
      return false;
    }
    if (searchTerm.trim() !== '') {
      const term = searchTerm.toLowerCase();
      return (
        item.details.toLowerCase().includes(term) ||
        item.action.toLowerCase().includes(term) ||
        item.user.toLowerCase().includes(term) ||
        item.clientIp.toLowerCase().includes(term)
      );
    }
    return true;
  });

  const handleExport = () => {
    // Format as security.log text format: [Timestamp] [LEVEL] [Oturum:X] [Kullanici:Y] [IP:Z] [Eylem:W] - Details
    const textData = logs
      .map(
        (l) =>
          `[${l.timestamp}] [${l.level}] [Oturum:${l.sessionId}] [Kullanici:${l.user}] [IP:${l.clientIp}] [Eylem:${l.action}] - ${l.details}`
      )
      .join('\n');

    const downloadAnchor = document.createElement('a');
    downloadAnchor.setAttribute('href', 'data:text/plain;charset=utf-8,' + encodeURIComponent(textData));
    downloadAnchor.setAttribute('download', `security_${new Date().toISOString().slice(0, 10)}.log`);
    document.body.appendChild(downloadAnchor);
    downloadAnchor.click();
    downloadAnchor.remove();
  };

  return (
    <div className="bg-[#111827] border border-[#334155] rounded-xl p-5 shadow-lg space-y-4 text-slate-200">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-[#1E293B] pb-4">
        <div className="flex items-center gap-3">
          <div className="w-9 h-9 rounded-lg bg-blue-600/20 border border-blue-500/30 flex items-center justify-center text-blue-400">
            <FileText className="w-5 h-5" />
          </div>
          <div>
            <h3 className="font-semibold text-white text-base">Güvenlik Denetim Günlüğü (security.log)</h3>
            <p className="text-xs text-slate-400">
              Dosya: <span className="font-mono text-emerald-400">C:\ProgramData\OzgurGuard\security.log</span> &bull; Kaynak:{' '}
              <span className="font-mono text-slate-300">OzgurGuard</span>
            </p>
          </div>
        </div>

        {/* Toolbar */}
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={handleExport}
            className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs font-medium rounded-lg border border-slate-700 transition-colors flex items-center gap-1.5 cursor-pointer"
            title="security.log Dosyasını İndir"
          >
            <Download className="w-3.5 h-3.5" />
            <span>Log Dosyasını İndir</span>
          </button>
          <button
            type="button"
            onClick={onClearLogs}
            className="px-3 py-1.5 bg-slate-800 hover:bg-red-950/40 text-red-400 hover:text-red-300 text-xs font-medium rounded-lg border border-slate-700 hover:border-red-500/40 transition-colors flex items-center gap-1.5 cursor-pointer"
            title="Günlükleri Temizle"
          >
            <Trash2 className="w-3.5 h-3.5" />
            <span>Temizle</span>
          </button>
        </div>
      </div>

      {/* Filter and Search Bar */}
      <div className="flex flex-wrap items-center justify-between gap-3 bg-slate-900/60 p-2.5 rounded-lg border border-slate-800 text-xs">
        <div className="flex items-center gap-1.5">
          <Filter className="w-3.5 h-3.5 text-slate-500 mr-1" />
          {(['All', 'INFO', 'WARN', 'ERROR'] as const).map((level) => (
            <button
              key={level}
              type="button"
              onClick={() => setFilterLevel(level)}
              className={`px-2.5 py-1 rounded-md text-xs font-medium transition-colors cursor-pointer ${
                filterLevel === level
                  ? 'bg-blue-600 text-white shadow-sm'
                  : 'bg-slate-800/80 text-slate-400 hover:text-slate-200 hover:bg-slate-800'
              }`}
            >
              {level === 'All' ? 'Tümü' : level}
            </button>
          ))}
        </div>

        <div className="relative min-w-[200px]">
          <Search className="w-3.5 h-3.5 text-slate-500 absolute left-2.5 top-1/2 -translate-y-1/2" />
          <input
            type="text"
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            placeholder="Log veya eylem ara..."
            className="w-full bg-slate-950 border border-slate-700 rounded-md pl-8 pr-3 py-1 text-xs text-white placeholder-slate-500 focus:outline-none focus:border-blue-500"
          />
        </div>
      </div>

      {/* Event List Table */}
      <div className="overflow-x-auto border border-slate-800 rounded-lg">
        <table className="w-full text-left text-xs font-mono">
          <thead className="bg-slate-900/90 text-slate-400 border-b border-slate-800">
            <tr>
              <th className="py-2 px-3 w-10">Düzey</th>
              <th className="py-2 px-3 w-44">Zaman</th>
              <th className="py-2 px-3 w-20">Oturum</th>
              <th className="py-2 px-3 w-28">Kullanıcı</th>
              <th className="py-2 px-3 w-32">İstemci IP</th>
              <th className="py-2 px-3 w-36">Eylem (Action)</th>
              <th className="py-2 px-3">Açıklama (Details)</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-800/60 bg-slate-950/40">
            {filteredLogs.length === 0 ? (
              <tr>
                <td colSpan={7} className="text-center py-8 text-slate-500">
                  Kayıtlı olay bulunamadı.
                </td>
              </tr>
            ) : (
              filteredLogs.map((log) => (
                <tr key={log.id} className="hover:bg-slate-900/50 transition-colors">
                  <td className="py-2 px-3">
                    {log.level === 'INFO' && (
                      <span title="Information">
                        <Info className="w-4 h-4 text-blue-400" />
                      </span>
                    )}
                    {log.level === 'WARN' && (
                      <span title="Warning">
                        <AlertTriangle className="w-4 h-4 text-amber-400" />
                      </span>
                    )}
                    {log.level === 'ERROR' && (
                      <span title="Error">
                        <AlertCircle className="w-4 h-4 text-red-400" />
                      </span>
                    )}
                  </td>
                  <td className="py-2 px-3 text-slate-400 text-[11px] whitespace-nowrap">{log.timestamp}</td>
                  <td className="py-2 px-3 text-blue-400">#{log.sessionId}</td>
                  <td className="py-2 px-3 text-slate-300 font-semibold">{log.user}</td>
                  <td className="py-2 px-3 text-slate-400">{log.clientIp}</td>
                  <td className="py-2 px-3 text-amber-400 font-semibold">{log.action}</td>
                  <td className="py-2 px-3 text-slate-200 leading-snug break-all">{log.details}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
};
