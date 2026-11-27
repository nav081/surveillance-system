import { useCallback, useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { useParams, Link as RouterLink } from 'react-router-dom';
import {
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  IconButton,
  MenuItem,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Tooltip,
  Typography,
  Box,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import DeleteIcon from '@mui/icons-material/Delete';
import EditIcon from '@mui/icons-material/Edit';

import { api } from '../api/client';
import type { CameraDto, CameraStatus, EntryDirection, HomeDto, UpsertCameraRequest } from '../api/types';
import { ErrorBanner, PageHeader, errorMessage, formatDateTime } from '../components/common';

interface CameraFormState {
  name: string;
  rtspUrl: string;
  location: string;
  status: CameraStatus;
  entryDirection: EntryDirection;
  linePosition: string;
}

const EMPTY_FORM: CameraFormState = {
  name: '',
  rtspUrl: '',
  location: '',
  status: 'Active',
  entryDirection: 'TopToBottom',
  linePosition: '0.5',
};

function CameraFormDialog({
  open,
  homeId,
  initial,
  onClose,
  onSaved,
}: {
  open: boolean;
  homeId: string;
  initial: CameraDto | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState<CameraFormState>(EMPTY_FORM);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!open) return;
    setError(null);
    setForm(
      initial
        ? {
            name: initial.name,
            // The API never returns the stored RTSP URL, so it must be re-entered on update.
            rtspUrl: '',
            location: initial.location ?? '',
            status: initial.status,
            entryDirection: initial.entryDirection,
            linePosition: String(initial.linePosition),
          }
        : EMPTY_FORM,
    );
  }, [open, initial]);

  const set = <K extends keyof CameraFormState>(key: K, value: CameraFormState[K]) =>
    setForm((f) => ({ ...f, [key]: value }));

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    const line = Number(form.linePosition);
    if (Number.isNaN(line) || line < 0.05 || line > 0.95) {
      setError('Line position must be between 0.05 and 0.95.');
      return;
    }
    const body: UpsertCameraRequest = {
      name: form.name.trim(),
      rtspUrl: form.rtspUrl.trim(),
      location: form.location.trim() || null,
      status: form.status,
      entryDirection: form.entryDirection,
      linePosition: line,
    };
    setBusy(true);
    setError(null);
    try {
      if (initial) await api.updateCamera(initial.id, body);
      else await api.createCamera(homeId, body);
      onSaved();
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm" component="form" onSubmit={onSubmit}>
      <DialogTitle>{initial ? 'Edit camera' : 'Add camera'}</DialogTitle>
      <DialogContent>
        <ErrorBanner error={error} />
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField label="Camera name" required value={form.name} onChange={(e) => set('name', e.target.value)} />
          <TextField
            label="RTSP URL"
            required
            value={form.rtspUrl}
            onChange={(e) => set('rtspUrl', e.target.value)}
            placeholder="rtsp://user:pass@192.168.1.10:554/stream1"
            helperText={initial ? 'Re-enter the RTSP URL to save changes. Stored URLs are never shown.' : undefined}
          />
          <TextField label="Location" value={form.location} onChange={(e) => set('location', e.target.value)} />
          <TextField select label="Status" value={form.status} onChange={(e) => set('status', e.target.value as CameraStatus)}>
            <MenuItem value="Active">Active</MenuItem>
            <MenuItem value="Inactive">Inactive</MenuItem>
          </TextField>
          <TextField
            select
            label="Entry direction"
            value={form.entryDirection}
            onChange={(e) => set('entryDirection', e.target.value as EntryDirection)}
            helperText="Direction of movement across the counting line that counts as an entry."
          >
            <MenuItem value="TopToBottom">Top to bottom</MenuItem>
            <MenuItem value="BottomToTop">Bottom to top</MenuItem>
          </TextField>
          <TextField
            label="Line position"
            type="number"
            value={form.linePosition}
            onChange={(e) => set('linePosition', e.target.value)}
            slotProps={{ htmlInput: { min: 0.05, max: 0.95, step: 0.01 } }}
            helperText="Fraction of frame height (0.05 to 0.95) where the counting line sits."
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button type="submit" variant="contained" disabled={busy}>Save</Button>
      </DialogActions>
    </Dialog>
  );
}

export function HomeDetailPage() {
  const { homeId = '' } = useParams();
  const [home, setHome] = useState<HomeDto | null>(null);
  const [cameras, setCameras] = useState<CameraDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<CameraDto | null>(null);
  const [toDelete, setToDelete] = useState<CameraDto | null>(null);

  const load = useCallback(() => {
    Promise.all([api.getHome(homeId), api.listCameras(homeId)])
      .then(([h, c]) => { setHome(h); setCameras(c); setError(null); })
      .catch((e) => setError(errorMessage(e)));
  }, [homeId]);

  useEffect(load, [load]);

  async function confirmDelete() {
    if (!toDelete) return;
    try {
      await api.deleteCamera(toDelete.id);
      setToDelete(null);
      load();
    } catch (e) {
      setError(errorMessage(e));
      setToDelete(null);
    }
  }

  return (
    <>
      <PageHeader
        title={home ? home.name : 'Home'}
        actions={
          <Stack direction="row" spacing={1}>
            <Button component={RouterLink} to="/homes" startIcon={<ArrowBackIcon />}>Homes</Button>
            <Button variant="contained" startIcon={<AddIcon />} onClick={() => { setEditing(null); setFormOpen(true); }}>
              Add camera
            </Button>
          </Stack>
        }
      />
      <ErrorBanner error={error} />
      {home && (
        <Typography color="text.secondary" sx={{ mb: 2 }}>
          {home.address || 'No address'}
        </Typography>
      )}
      {!cameras && !error && <CircularProgress />}
      {cameras && (
        <TableContainer component={Paper}>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Name</TableCell>
                <TableCell>Location</TableCell>
                <TableCell>RTSP URL</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>Online</TableCell>
                <TableCell>Last heartbeat</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {cameras.length === 0 && (
                <TableRow>
                  <TableCell colSpan={7} align="center">No cameras in this home yet.</TableCell>
                </TableRow>
              )}
              {cameras.map((cam) => (
                <TableRow key={cam.id} hover>
                  <TableCell>{cam.name}</TableCell>
                  <TableCell>{cam.location || '—'}</TableCell>
                  <TableCell><Box component="code" sx={{ fontSize: 12 }}>{cam.rtspUrlMasked}</Box></TableCell>
                  <TableCell>
                    <Chip size="small" label={cam.status} color={cam.status === 'Active' ? 'success' : 'default'} />
                  </TableCell>
                  <TableCell>
                    <Chip size="small" label={cam.isOnline ? 'Online' : 'Offline'} color={cam.isOnline ? 'primary' : 'warning'} variant="outlined" />
                  </TableCell>
                  <TableCell>{formatDateTime(cam.lastHeartbeatAt)}</TableCell>
                  <TableCell align="right">
                    <Tooltip title="Edit">
                      <IconButton size="small" onClick={() => { setEditing(cam); setFormOpen(true); }} aria-label="edit camera">
                        <EditIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                    <Tooltip title="Delete">
                      <IconButton size="small" color="error" onClick={() => setToDelete(cam)} aria-label="delete camera">
                        <DeleteIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}

      <CameraFormDialog
        open={formOpen}
        homeId={homeId}
        initial={editing}
        onClose={() => setFormOpen(false)}
        onSaved={() => { setFormOpen(false); load(); }}
      />

      <Dialog open={toDelete !== null} onClose={() => setToDelete(null)}>
        <DialogTitle>Delete camera?</DialogTitle>
        <DialogContent>
          <DialogContentText>
            "{toDelete?.name}" will be removed. Existing attendance records are kept.
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setToDelete(null)}>Cancel</Button>
          <Button color="error" variant="contained" onClick={confirmDelete}>Delete</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
