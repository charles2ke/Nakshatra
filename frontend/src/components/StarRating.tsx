import type { CSSProperties } from 'react';
import { useLocale } from '../i18n/LocaleContext';

interface Props { rating: number; count?: number; testId?: string; }

/** Five stars filled in proportion to the rating, with an optional number of ratings. */
export default function StarRating({ rating, count, testId }: Props) {
  const { t, formatNumber } = useLocale();
  const value = Math.round(Math.min(5, Math.max(0, rating)) * 10) / 10;
  const fill = { '--rating-fill': `${(value / 5) * 100}%` } as CSSProperties;
  return (
    <span className="star-rating" data-testid={testId}>
      <span className="rating-value" aria-hidden="true">{formatNumber(value)}</span>
      <span className="stars" role="img" aria-label={t('rating.outOfFive', { rating: formatNumber(value) })} style={fill}>★★★★★</span>
      {count !== undefined && (
        <span className="rating-count">{count === 1 ? t('rating.countOne') : t('rating.count', { count: formatNumber(count) })}</span>
      )}
    </span>
  );
}
