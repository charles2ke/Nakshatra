import { useEffect, useId, useRef, useState } from 'react';
import type { InputHTMLAttributes, KeyboardEvent } from 'react';
import { searchSuggest } from '../api/client';
import { useLocale } from '../i18n/LocaleContext';

interface Props extends Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'onSelect'> {
  value: string;
  onChange: (value: string) => void;
  /** Called when the user picks a suggestion with the mouse or keyboard. */
  onSelect: (value: string) => void;
  testId: string;
}

const MIN_CHARS = 2;
const DEBOUNCE_MS = 250;

/** A text input that offers type-ahead suggestions from the search service (ARIA combobox pattern). */
export default function SuggestInput({ value, onChange, onSelect, testId, onKeyDown, onBlur, ...inputProps }: Props) {
  const { t } = useLocale();
  const listId = useId();
  const [suggestions, setSuggestions] = useState<string[]>([]);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  // Bumped to invalidate lookups in flight (on typing, closing or choosing).
  const request = useRef(0);

  useEffect(() => () => clearTimeout(timer.current), []);

  function cancelLookup() {
    clearTimeout(timer.current);
    request.current++;
  }

  // Also drops any lookup in flight, so a late response cannot reopen the list after a submit or blur.
  function close() {
    cancelLookup();
    setOpen(false);
    setActive(-1);
  }

  function handleChange(next: string) {
    onChange(next);
    cancelLookup();
    const term = next.trim();
    if (term.length < MIN_CHARS) {
      close();
      return;
    }
    const version = request.current;
    timer.current = setTimeout(() => {
      searchSuggest(term)
        .then(list => {
          if (request.current !== version) return;
          const items = Array.isArray(list) ? [...new Set(list.filter(s => typeof s === 'string'))] : [];
          setSuggestions(items);
          setActive(-1);
          setOpen(items.length > 0);
        })
        .catch(() => {});
    }, DEBOUNCE_MS);
  }

  function choose(suggestion: string) {
    close();
    onSelect(suggestion);
  }

  function handleKeyDown(e: KeyboardEvent<HTMLInputElement>) {
    if (open && (e.key === 'ArrowDown' || e.key === 'ArrowUp')) {
      e.preventDefault();
      const last = suggestions.length - 1;
      const down = e.key === 'ArrowDown';
      setActive(i => (down ? (i >= last ? 0 : i + 1) : (i <= 0 ? last : i - 1)));
      return;
    }
    if (open && e.key === 'Enter' && active >= 0) {
      e.preventDefault();
      choose(suggestions[active]);
      return;
    }
    if (open && e.key === 'Escape') {
      e.preventDefault();
      close();
      return;
    }
    if (!open && e.key === 'ArrowDown' && suggestions.length > 0 && value.trim().length >= MIN_CHARS) {
      e.preventDefault();
      setOpen(true);
      setActive(0);
      return;
    }
    // Enter without a highlighted suggestion submits the surrounding form as typed.
    if (e.key === 'Enter') close();
    onKeyDown?.(e);
  }

  return (
    <div className="suggest">
      <input
        {...inputProps}
        data-testid={testId}
        role="combobox"
        aria-autocomplete="list"
        aria-expanded={open}
        aria-controls={listId}
        aria-activedescendant={open && active >= 0 ? `${listId}-${active}` : undefined}
        autoComplete="off"
        value={value}
        onChange={e => handleChange(e.target.value)}
        onKeyDown={handleKeyDown}
        onBlur={e => { close(); onBlur?.(e); }}
      />
      <ul id={listId} role="listbox" className="suggest-list" aria-label={t('search.suggestions')} hidden={!open} data-testid={`${testId}-suggestions`}>
        {suggestions.map((s, i) => (
          <li
            key={s}
            id={`${listId}-${i}`}
            role="option"
            aria-selected={i === active}
            className={i === active ? 'suggest-option active' : 'suggest-option'}
            data-testid={`${testId}-suggestion-${i}`}
            // Keep focus in the input so choosing does not blur it first.
            onMouseDown={e => e.preventDefault()}
            onClick={() => choose(s)}
          >
            {s}
          </li>
        ))}
      </ul>
    </div>
  );
}
