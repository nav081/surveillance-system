import { useEffect, useState } from 'react';
import {
  CircularProgress,
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TablePagination,
  TableRow,
  Typography,
} from '@mui/material';

import { api } from '../api/client';
import type { AuditLogDto, PagedResult } from '../api/types';
import { ErrorBanner, PageHeader, errorMessage, formatDateTime } from '../components/common';

export function AuditPage() {
  const [page, setPage] = useState(0);
  const pageSize = 25;
  const [data, setData] = useState<PagedResult<AuditLogDto> | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    api.listAuditLogs(page + 1, pageSize)
      .then((d) => { if (!cancelled) { setData(d); setError(null); } })
      .catch((e) => { if (!cancelled) setError(errorMessage(e)); });
    return () => { cancelled = true; };
  }, [page]);

  return (
    <>
      <PageHeader title="Audit log" />
      <ErrorBanner error={error} />
      {!data && !error && <CircularProgress />}
      {data && (
        <TableContainer component={Paper}>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Time</TableCell>
                <TableCell>Actor</TableCell>
                <TableCell>Action</TableCell>
                <TableCell>Entity</TableCell>
                <TableCell>IP</TableCell>
                <TableCell>Changes</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {data.items.length === 0 && (
                <TableRow><TableCell colSpan={6} align="center"><Typography color="text.secondary">No audit entries.</Typography></TableCell></TableRow>
              )}
              {data.items.map((a) => (
                <TableRow key={a.id} hover>
                  <TableCell>{formatDateTime(a.timestamp)}</TableCell>
                  <TableCell>{a.actor}</TableCell>
                  <TableCell>{a.action}</TableCell>
                  <TableCell>{a.entityType}{a.entityId ? ` ${a.entityId.slice(0, 8)}…` : ''}</TableCell>
                  <TableCell>{a.ipAddress ?? '—'}</TableCell>
                  <TableCell sx={{ maxWidth: 320, wordBreak: 'break-all', fontSize: 12 }}>{a.changes ?? '—'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <TablePagination
            component="div"
            count={data.totalCount}
            page={page}
            rowsPerPage={pageSize}
            rowsPerPageOptions={[pageSize]}
            onPageChange={(_, p) => setPage(p)}
          />
        </TableContainer>
      )}
    </>
  );
}
