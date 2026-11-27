import { useEffect, useState } from 'react';
import {
  Button,
  CircularProgress,
  IconButton,
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tooltip,
  Typography,
  Alert,
} from '@mui/material';
import DeleteIcon from '@mui/icons-material/Delete';

import { api } from '../api/client';
import type { PersonDto } from '../api/types';
import { ErrorBanner, PageHeader, errorMessage, formatDateTime } from '../components/common';

export function PersonsPage() {
  const [persons, setPersons] = useState<PersonDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  function load() {
    api.listPersons()
      .then((p) => { setPersons(p); setError(null); })
      .catch((e) => setError(errorMessage(e)));
  }

  useEffect(load, []);

  async function remove(p: PersonDto) {
    if (!window.confirm(`Remove enrolled person "${p.displayName}"? Their face embeddings will be deleted.`)) return;
    try {
      await api.deletePerson(p.id);
      load();
    } catch (e) {
      setError(errorMessage(e));
    }
  }

  return (
    <>
      <PageHeader title="Enrolled persons" />
      <Alert severity="info" sx={{ mb: 2 }}>
        New people are enrolled from a face photo with the CLI tool <code>monitoring/enroll.py</code>.
        Only the face embedding is stored; the photo is not kept.
      </Alert>
      <ErrorBanner error={error} />
      {!persons && !error && <CircularProgress />}
      {persons && (
        <TableContainer component={Paper}>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Name</TableCell>
                <TableCell>Enrolled</TableCell>
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {persons.length === 0 && (
                <TableRow><TableCell colSpan={3} align="center"><Typography color="text.secondary">No persons enrolled.</Typography></TableCell></TableRow>
              )}
              {persons.map((p) => (
                <TableRow key={p.id} hover>
                  <TableCell>{p.displayName}</TableCell>
                  <TableCell>{formatDateTime(p.createdAt)}</TableCell>
                  <TableCell align="right">
                    <Tooltip title="Delete">
                      <IconButton size="small" color="error" onClick={() => remove(p)} aria-label="delete person">
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
      <Button sx={{ mt: 2 }} variant="text" onClick={load}>Refresh</Button>
    </>
  );
}
