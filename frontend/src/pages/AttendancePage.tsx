import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Box,
  Button,
  Dialog,
  DialogContent,
  DialogTitle,
  Grid,
  MenuItem,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TablePagination,
  TableRow,
  TextField,
  Typography,
  Card,
  CardContent,
  CircularProgress,
  IconButton,
  Tooltip,
} from '@mui/material';
import DownloadIcon from '@mui/icons-material/Download';
import ImageIcon from '@mui/icons-material/Image';

import { api } from '../api/client';
import type { AttendanceDto, DailyAttendanceSummaryDto, HomeDto } from '../api/types';
import {
  ErrorBanner,
  PageHeader,
  downloadBlob,
  errorMessage,
  formatDateTime,
  formatDuration,
  useObjectUrl,
} from '../components/common';

/** Converts a yyyy-mm-dd local date to an ISO timestamp at the start (or end) of that day. */
function localDayBoundary(date: string, end: boolean): string {
  const d = new Date(`${date}T00:00:00`);
  if (end) d.setDate(d.getDate() + 1);
  return d.toISOString();
}

function isoDate(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

function SnapshotDialog({ recordId, kind, onClose }: { recordId: string | null; kind: 'entry' | 'exit'; onClose: () => void }) {
  const loader = useCallback(
    () => (recordId ? api.snapshot(recordId, kind) : Promise.reject(new Error('No record'))),
    [recordId, kind],
  );
  const { url, error } = useObjectUrl(recordId ? loader : null);
  return (
    <Dialog open={recordId !== null} onClose={onClose} maxWidth="md" fullWidth>
      <DialogTitle>{kind === 'entry' ? 'Entry snapshot' : 'Exit snapshot'}</DialogTitle>
      <DialogContent>
        {error && <Typography color="error">{error}</Typography>}
        {!url && !error && <CircularProgress />}
        {url && <Box component="img" src={url} alt={`${kind} snapshot`} sx={{ width: '100%', borderRadius: 1 }} />}
      </DialogContent>
    </Dialog>
  );
}

export function AttendancePage() {
  const today = useMemo(() => new Date(), []);
  const weekAgo = useMemo(() => new Date(Date.now() - 6 * 24 * 3600 * 1000), []);

  const [homes, setHomes] = useState<HomeDto[]>([]);
  const [homeId, setHomeId] = useState('');
  const [fromDate, setFromDate] = useState(isoDate(weekAgo));
  const [toDate, setToDate] = useState(isoDate(today));
  const [personKey, setPersonKey] = useState('');

  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(25);
  const [rows, setRows] = useState<AttendanceDto[]>([]);
  const [total, setTotal] = useState(0);
  const [summary, setSummary] = useState<DailyAttendanceSummaryDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [snapshot, setSnapshot] = useState<{ id: string; kind: 'entry' | 'exit' } | null>(null);

  useEffect(() => {
    api.listHomes().then(setHomes).catch(() => setHomes([]));
  }, []);

  const filters = useMemo(
    () => ({
      homeId: homeId || undefined,
      from: fromDate ? localDayBoundary(fromDate, false) : undefined,
      to: toDate ? localDayBoundary(toDate, true) : undefined,
      personKey: personKey.trim() || undefined,
    }),
    [homeId, fromDate, toDate, personKey],
  );

  useEffect(() => { setPage(0); }, [filters]);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    const listReq = api.listAttendance({ ...filters, page: page + 1, pageSize });
    const summaryReq =
      filters.from && filters.to
        ? api.attendanceSummary({ homeId: filters.homeId, from: filters.from, to: filters.to })
        : Promise.resolve([] as DailyAttendanceSummaryDto[]);
    Promise.all([listReq, summaryReq])
      .then(([list, sum]) => {
        if (cancelled) return;
        setRows(list.items);
        setTotal(list.totalCount);
        setSummary(sum);
        setError(null);
      })
      .catch((e) => { if (!cancelled) setError(errorMessage(e)); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [filters, page, pageSize]);

  async function exportCsv() {
    try {
      const blob = await api.exportAttendanceCsv({ homeId: filters.homeId, from: filters.from, to: filters.to });
      downloadBlob(blob, `attendance-${fromDate}-${toDate}.csv`);
    } catch (e) {
      setError(errorMessage(e));
    }
  }

  const totals = summary.reduce(
    (acc, d) => ({ entries: acc.entries + d.entries, exits: acc.exits + d.exits }),
    { entries: 0, exits: 0 },
  );

  return (
    <>
      <PageHeader
        title="Attendance"
        actions={
          <Button variant="outlined" startIcon={<DownloadIcon />} onClick={exportCsv}>
            Export CSV
          </Button>
        }
      />
      <ErrorBanner error={error} />

      <Paper sx={{ p: 2, mb: 2 }}>
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
          <TextField select label="Home" value={homeId} onChange={(e) => setHomeId(e.target.value)} sx={{ minWidth: 200 }}>
            <MenuItem value="">All homes</MenuItem>
            {homes.map((h) => (
              <MenuItem key={h.id} value={h.id}>{h.name}</MenuItem>
            ))}
          </TextField>
          <TextField type="date" label="From" InputLabelProps={{ shrink: true }} value={fromDate} onChange={(e) => setFromDate(e.target.value)} />
          <TextField type="date" label="To" InputLabelProps={{ shrink: true }} value={toDate} onChange={(e) => setToDate(e.target.value)} />
          <TextField label="Person key" value={personKey} onChange={(e) => setPersonKey(e.target.value)} />
        </Stack>
      </Paper>

      <Grid container spacing={2} sx={{ mb: 2 }}>
        <Grid item xs={12} sm={4}>
          <Card><CardContent><Typography variant="body2" color="text.secondary">Entries in range</Typography><Typography variant="h5">{totals.entries}</Typography></CardContent></Card>
        </Grid>
        <Grid item xs={12} sm={4}>
          <Card><CardContent><Typography variant="body2" color="text.secondary">Exits in range</Typography><Typography variant="h5">{totals.exits}</Typography></CardContent></Card>
        </Grid>
        <Grid item xs={12} sm={4}>
          <Card><CardContent><Typography variant="body2" color="text.secondary">Average stay (days with data)</Typography>
            <Typography variant="h5">
              {formatDuration(
                summary.length
                  ? Math.round(summary.reduce((a, d) => a + d.averageDurationSeconds, 0) / summary.length)
                  : null,
              )}
            </Typography>
          </CardContent></Card>
        </Grid>
      </Grid>

      <TableContainer component={Paper} sx={{ position: 'relative' }}>
        {loading && <Box sx={{ position: 'absolute', top: 8, right: 8 }}><CircularProgress size={20} /></Box>}
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Person</TableCell>
              <TableCell>Entry</TableCell>
              <TableCell>Exit</TableCell>
              <TableCell>Duration</TableCell>
              <TableCell align="right">Snapshots</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.length === 0 && !loading && (
              <TableRow><TableCell colSpan={5} align="center">No attendance records for this range.</TableCell></TableRow>
            )}
            {rows.map((r) => (
              <TableRow key={r.id} hover>
                <TableCell>
                  {r.displayName}
                  <Typography variant="caption" color="text.secondary" display="block">{r.personKey}</Typography>
                </TableCell>
                <TableCell>{formatDateTime(r.entryTime)}</TableCell>
                <TableCell>{r.exitTime ? formatDateTime(r.exitTime) : <em>Inside</em>}</TableCell>
                <TableCell>{formatDuration(r.durationSeconds)}</TableCell>
                <TableCell align="right">
                  <Tooltip title="Entry snapshot">
                    <span>
                      <IconButton size="small" disabled={!r.hasEntrySnapshot} onClick={() => setSnapshot({ id: r.id, kind: 'entry' })} aria-label="entry snapshot">
                        <ImageIcon fontSize="small" />
                      </IconButton>
                    </span>
                  </Tooltip>
                  <Tooltip title="Exit snapshot">
                    <span>
                      <IconButton size="small" disabled={!r.hasExitSnapshot} onClick={() => setSnapshot({ id: r.id, kind: 'exit' })} aria-label="exit snapshot">
                        <ImageIcon fontSize="small" color="secondary" />
                      </IconButton>
                    </span>
                  </Tooltip>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        <TablePagination
          component="div"
          count={total}
          page={page}
          onPageChange={(_, p) => setPage(p)}
          rowsPerPage={pageSize}
          onRowsPerPageChange={(e) => { setPageSize(Number(e.target.value)); setPage(0); }}
          rowsPerPageOptions={[10, 25, 50, 100]}
        />
      </TableContainer>

      <SnapshotDialog
        recordId={snapshot?.id ?? null}
        kind={snapshot?.kind ?? 'entry'}
        onClose={() => setSnapshot(null)}
      />
    </>
  );
}
