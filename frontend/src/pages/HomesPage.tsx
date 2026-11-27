import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import {
  Button,
  Card,
  CardActions,
  CardContent,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Grid,
  IconButton,
  Stack,
  TextField,
  Tooltip,
  Typography,
  Box,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import DeleteIcon from '@mui/icons-material/Delete';
import EditIcon from '@mui/icons-material/Edit';

import { api } from '../api/client';
import type { HomeDto } from '../api/types';
import { ErrorBanner, PageHeader, errorMessage } from '../components/common';

interface HomeFormProps {
  open: boolean;
  initial: HomeDto | null;
  onClose: () => void;
  onSaved: () => void;
}

function HomeFormDialog({ open, initial, onClose, onSaved }: HomeFormProps) {
  const [name, setName] = useState('');
  const [address, setAddress] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (open) {
      setName(initial?.name ?? '');
      setAddress(initial?.address ?? '');
      setError(null);
    }
  }, [open, initial]);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const addr = address.trim() || null;
      if (initial) await api.updateHome(initial.id, name.trim(), addr);
      else await api.createHome(name.trim(), addr);
      onSaved();
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm" component="form" onSubmit={onSubmit}>
      <DialogTitle>{initial ? 'Edit home' : 'New home'}</DialogTitle>
      <DialogContent>
        <ErrorBanner error={error} />
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField label="Name" required value={name} onChange={(e) => setName(e.target.value)} />
          <TextField label="Address" value={address} onChange={(e) => setAddress(e.target.value)} />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button type="submit" variant="contained" disabled={busy}>Save</Button>
      </DialogActions>
    </Dialog>
  );
}

export function HomesPage() {
  const [homes, setHomes] = useState<HomeDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<HomeDto | null>(null);
  const [formOpen, setFormOpen] = useState(false);

  function load() {
    api.listHomes()
      .then((h) => { setHomes(h); setError(null); })
      .catch((e) => setError(errorMessage(e)));
  }

  useEffect(load, []);

  async function remove(home: HomeDto) {
    if (!window.confirm(`Delete "${home.name}" and all of its cameras?`)) return;
    try {
      await api.deleteHome(home.id);
      load();
    } catch (e) {
      setError(errorMessage(e));
    }
  }

  return (
    <>
      <PageHeader
        title="Homes"
        actions={
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => { setEditing(null); setFormOpen(true); }}>
            New home
          </Button>
        }
      />
      <ErrorBanner error={error} />
      {!homes && <CircularProgress />}
      {homes && homes.length === 0 && (
        <Typography color="text.secondary">No homes yet. Create one to start adding cameras.</Typography>
      )}
      <Grid container spacing={2}>
        {homes?.map((home) => (
          <Grid item xs={12} sm={6} md={4} key={home.id}>
            <Card>
              <CardContent>
                <Typography variant="h6" fontWeight={600}>{home.name}</Typography>
                <Typography variant="body2" color="text.secondary">{home.address || 'No address'}</Typography>
                <Box sx={{ mt: 1 }}>
                  <Typography variant="body2">
                    Cameras: {home.cameraCount} ({home.onlineCameraCount} online)
                  </Typography>
                </Box>
              </CardContent>
              <CardActions>
                <Button size="small" component={RouterLink} to={`/homes/${home.id}`}>Open</Button>
                <Tooltip title="Edit">
                  <IconButton size="small" onClick={() => { setEditing(home); setFormOpen(true); }} aria-label="edit home">
                    <EditIcon fontSize="small" />
                  </IconButton>
                </Tooltip>
                <Tooltip title="Delete">
                  <IconButton size="small" color="error" onClick={() => remove(home)} aria-label="delete home">
                    <DeleteIcon fontSize="small" />
                  </IconButton>
                </Tooltip>
              </CardActions>
            </Card>
          </Grid>
        ))}
      </Grid>
      <HomeFormDialog
        open={formOpen}
        initial={editing}
        onClose={() => setFormOpen(false)}
        onSaved={() => { setFormOpen(false); load(); }}
      />
    </>
  );
}
