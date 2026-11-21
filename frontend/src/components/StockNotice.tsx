import { LOW_STOCK_THRESHOLD } from '../api/client';
import { useLocale } from '../i18n/LocaleContext';

interface Props { stock: number; testId?: string; /** Say nothing while plenty is in stock. */ onlyWhenLow?: boolean; }

export default function StockNotice({ stock, testId, onlyWhenLow }: Props) {
  const { t, formatNumber } = useLocale();
  if (stock <= 0) return <div className="stock-notice stock-out" data-testid={testId}>{t('stock.out')}</div>;
  if (stock <= LOW_STOCK_THRESHOLD) {
    return <div className="stock-notice stock-low" data-testid={testId}>{t('stock.low', { count: formatNumber(stock) })}</div>;
  }
  if (onlyWhenLow) return null;
  return <div className="stock-notice stock-in" data-testid={testId}>{t('stock.in')}</div>;
}
